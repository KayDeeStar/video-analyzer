# C# API: one Lambda (ASP.NET Core Minimal API) behind an API Gateway HTTP API.

locals {
  function_name = "${var.name_prefix}-api"
}

data "archive_file" "api" {
  type        = "zip"
  source_dir  = var.package_dir
  output_path = "${var.package_dir}.zip"
}

# ---------- IAM ----------

data "aws_iam_policy_document" "lambda_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "api" {
  name               = "${local.function_name}-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume.json
}

resource "aws_iam_role_policy_attachment" "api_logs" {
  role       = aws_iam_role.api.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

# Pre-signed URLs carry the permissions of the role that signs them, so this role
# must be allowed to do what the browser does with them (upload parts, read the video).
data "aws_iam_policy_document" "api_s3" {
  statement {
    sid = "UploadsAndJobs"
    actions = [
      "s3:PutObject",
      "s3:GetObject",
      "s3:DeleteObject",
      "s3:AbortMultipartUpload",
      "s3:ListMultipartUploadParts",
    ]
    resources = [
      "${var.media_bucket_arn}/uploads/*",
      "${var.media_bucket_arn}/jobs/*",
    ]
  }

  statement {
    sid       = "ReadResults"
    actions   = ["s3:GetObject"]
    resources = ["${var.media_bucket_arn}/results/*"]
  }

  # ListBucket also makes S3 return 404 (instead of 403) for missing objects.
  statement {
    sid       = "ListBucket"
    actions   = ["s3:ListBucket"]
    resources = [var.media_bucket_arn]
  }
}

resource "aws_iam_role_policy" "api_s3" {
  name   = "media-bucket-access"
  role   = aws_iam_role.api.id
  policy = data.aws_iam_policy_document.api_s3.json
}

# ---------- Lambda ----------

resource "aws_cloudwatch_log_group" "api" {
  name              = "/aws/lambda/${local.function_name}"
  retention_in_days = var.log_retention_days
}

resource "aws_lambda_function" "api" {
  function_name    = local.function_name
  description      = "Video Analyzer HTTP API (ASP.NET Core)"
  role             = aws_iam_role.api.arn
  runtime          = "dotnet10"
  architectures    = ["arm64"]
  handler          = "VideoAnalyzer.Api"
  filename         = data.archive_file.api.output_path
  source_code_hash = data.archive_file.api.output_base64sha256
  memory_size      = 1024
  timeout          = 29 # API Gateway gives up after 30 s

  environment {
    variables = {
      Storage__MediaBucket     = var.media_bucket_name
      Storage__MaxUploadSizeMb = tostring(var.max_upload_size_mb)
    }
  }

  depends_on = [
    aws_cloudwatch_log_group.api,
    aws_iam_role_policy_attachment.api_logs,
  ]
}

# ---------- API Gateway (HTTP API) ----------

# No CORS: browsers reach the API through CloudFront on the same origin as the site,
# so other websites cannot call it from their pages.
resource "aws_apigatewayv2_api" "api" {
  name          = local.function_name
  protocol_type = "HTTP"
}

resource "aws_apigatewayv2_integration" "api" {
  api_id                 = aws_apigatewayv2_api.api.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.api.invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "default" {
  api_id    = aws_apigatewayv2_api.api.id
  route_key = "$default"
  target    = "integrations/${aws_apigatewayv2_integration.api.id}"
}

resource "aws_cloudwatch_log_group" "api_access" {
  name              = "/aws/apigateway/${local.function_name}"
  retention_in_days = var.log_retention_days
}

resource "aws_apigatewayv2_stage" "default" {
  api_id      = aws_apigatewayv2_api.api.id
  name        = "$default"
  auto_deploy = true

  # No authentication yet, so cap request rates to limit abuse and cost.
  default_route_settings {
    throttling_burst_limit = var.throttling_burst_limit
    throttling_rate_limit  = var.throttling_rate_limit
  }

  access_log_settings {
    destination_arn = aws_cloudwatch_log_group.api_access.arn
    format = jsonencode({
      requestId        = "$context.requestId"
      ip               = "$context.identity.sourceIp"
      method           = "$context.httpMethod"
      path             = "$context.path"
      status           = "$context.status"
      latencyMs        = "$context.responseLatency"
      integrationError = "$context.integrationErrorMessage"
    })
  }
}

resource "aws_lambda_permission" "apigw" {
  statement_id  = "AllowApiGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.api.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.api.execution_arn}/*/*"
}
