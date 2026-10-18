# Analysis pipeline:
#   S3 "Object Created" under uploads/  ->  EventBridge rule  ->  Step Functions state machine
#   StartJob (Lambda) -> in parallel:
#       * Rekognition label detection (when people/objects are visible; polled)
#       * DescribeVideo (Lambda): Amazon Nova on Bedrock watches the video and writes a report
#   -> CollectResults (Lambda) writes results/{jobId}/result.json.  Any failure -> FailJob (Lambda).

locals {
  prefix = "${var.name_prefix}-analysis"

  functions = {
    start-job       = { handler = "StartJob", timeout = 30, memory = 512 }
    describe-video  = { handler = "DescribeVideo", timeout = 300, memory = 512 }
    collect-results = { handler = "CollectResults", timeout = 600, memory = 2048 }
    fail-job        = { handler = "FailJob", timeout = 30, memory = 512 }
  }
}

data "aws_caller_identity" "current" {}

data "archive_file" "processing" {
  type        = "zip"
  source_dir  = var.package_dir
  output_path = "${var.package_dir}.zip"
}

# ---------- Lambda functions ----------

data "aws_iam_policy_document" "lambda_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "functions" {
  name               = "${local.prefix}-functions-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume.json
}

resource "aws_iam_role_policy_attachment" "functions_logs" {
  role       = aws_iam_role.functions.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

data "aws_iam_policy_document" "functions" {
  statement {
    sid     = "JobsResultsAndWorkFiles"
    actions = ["s3:GetObject", "s3:PutObject"]
    resources = [
      "${var.media_bucket_arn}/jobs/*",
      "${var.media_bucket_arn}/results/*",
      "${var.media_bucket_arn}/work/*",
    ]
  }

  # Lets S3 answer 404 (not 403) for missing job records.
  statement {
    sid       = "ListBucket"
    actions   = ["s3:ListBucket"]
    resources = [var.media_bucket_arn]
  }

  statement {
    sid       = "ReadRekognitionResults"
    actions   = ["rekognition:GetLabelDetection"]
    resources = ["*"]
  }

  # Bedrock reads the video from S3 with the caller's (this role's) permissions.
  statement {
    sid       = "ReadUploadedVideos"
    actions   = ["s3:GetObject"]
    resources = ["${var.media_bucket_arn}/uploads/*"]
  }

  # Cross-region inference profiles (us.*) route to the foundation model in several US regions.
  statement {
    sid     = "InvokeNova"
    actions = ["bedrock:InvokeModel"]
    resources = [
      "arn:aws:bedrock:*::foundation-model/amazon.nova-*",
      "arn:aws:bedrock:*:${data.aws_caller_identity.current.account_id}:inference-profile/*amazon.nova-*",
    ]
  }
}

resource "aws_iam_role_policy" "functions" {
  name   = "analysis-functions"
  role   = aws_iam_role.functions.id
  policy = data.aws_iam_policy_document.functions.json
}

resource "aws_cloudwatch_log_group" "functions" {
  for_each          = local.functions
  name              = "/aws/lambda/${local.prefix}-${each.key}"
  retention_in_days = var.log_retention_days
}

resource "aws_lambda_function" "functions" {
  for_each = local.functions

  function_name    = "${local.prefix}-${each.key}"
  role             = aws_iam_role.functions.arn
  runtime          = "dotnet10"
  architectures    = ["arm64"]
  handler          = "VideoAnalyzer.Processing::VideoAnalyzer.Processing.Functions::${each.value.handler}"
  filename         = data.archive_file.processing.output_path
  source_code_hash = data.archive_file.processing.output_base64sha256
  memory_size      = each.value.memory
  timeout          = each.value.timeout

  environment {
    variables = {
      MEDIA_BUCKET     = var.media_bucket_name
      BEDROCK_MODEL_ID = var.bedrock_model_id
    }
  }

  depends_on = [
    aws_cloudwatch_log_group.functions,
    aws_iam_role_policy_attachment.functions_logs,
  ]
}

# ---------- State machine ----------

locals {
  lambda_retry = [{
    ErrorEquals     = ["Lambda.ServiceException", "Lambda.AWSLambdaException", "Lambda.SdkClientException", "Lambda.TooManyRequestsException"]
    IntervalSeconds = 2
    MaxAttempts     = 4
    BackoffRate     = 2
    JitterStrategy  = "FULL"
  }]

  rekognition_retry = [{
    ErrorEquals     = ["Rekognition.ThrottlingException", "Rekognition.ProvisionedThroughputExceededException", "Rekognition.LimitExceededException", "Rekognition.InternalServerErrorException"]
    IntervalSeconds = 10
    MaxAttempts     = 6
    BackoffRate     = 2
    JitterStrategy  = "FULL"
  }]

  # Every failure ends in MarkFailed, which records a readable error on the job.
  catch_all = [{
    ErrorEquals = ["States.ALL"]
    Next        = "MarkFailed"
    Output = {
      jobId = "{% $jobId %}"
      error = "{% $states.errorOutput.Error %}"
      cause = "{% $exists($states.errorOutput.Cause) ? $states.errorOutput.Cause : '' %}"
    }
  }]

  # Rekognition Video jobs to run in parallel; extra Start* arguments per job.
  # (Face and text detection were dropped: the Nova report describes people and reads text far better.)
  analyses = {
    Labels = {
      start = "startLabelDetection"
      get   = "getLabelDetection"
      args  = { MinConfidence = 60, Features = ["GENERAL_LABELS"] }
    }
  }

  # Each Rekognition branch: start the job, then poll until it succeeds or fails.
  rekognition_branches = [for name, a in local.analyses : {
    StartAt = "Start${name}"
    States = {
      "Start${name}" = {
        Type     = "Task"
        Resource = "arn:aws:states:::aws-sdk:rekognition:${a.start}"
        Arguments = merge({
          Video = { S3Object = { Bucket = "{% $states.input.bucket %}", Name = "{% $states.input.videoKey %}" } }
          # Same token on retries within this run (idempotent); a new run (re-analysis) gets a new token.
          ClientRequestToken = "{% $states.input.jobId & '-${lower(name)}-' & $states.input.runId %}"
        }, a.args)
        Assign = { rekognitionJobId = "{% $states.result.JobId %}" }
        Retry  = local.rekognition_retry
        Next   = "Wait${name}"
      }
      "Wait${name}" = {
        Type    = "Wait"
        Seconds = var.poll_interval_seconds
        Next    = "Check${name}"
      }
      "Check${name}" = {
        Type      = "Task"
        Resource  = "arn:aws:states:::aws-sdk:rekognition:${a.get}"
        Arguments = { JobId = "{% $rekognitionJobId %}", MaxResults = 1 }
        Output = {
          status  = "{% $states.result.JobStatus %}"
          message = "{% $exists($states.result.StatusMessage) ? $states.result.StatusMessage : '' %}"
        }
        Retry = local.rekognition_retry
        Next  = "${name}Finished?"
      }
      "${name}Finished?" = {
        Type = "Choice"
        Choices = [
          { Condition = "{% $states.input.status = 'SUCCEEDED' %}", Next = "${name}Succeeded" },
          { Condition = "{% $states.input.status = 'FAILED' %}", Next = "${name}Failed" },
        ]
        Default = "Wait${name}"
      }
      "${name}Succeeded" = {
        Type   = "Pass"
        Output = { (format("%sJobId", lower(name))) = "{% $rekognitionJobId %}" }
        End    = true
      }
      "${name}Failed" = {
        Type  = "Fail"
        Error = "Rekognition.${name}DetectionFailed"
        Cause = "{% $states.input.message %}"
      }
    }
  }]

  # Amazon Nova describes the whole video (people, clothing, actions, vehicles, timeline).
  describe_branch = {
    StartAt = "DescribeVideo"
    States = {
      DescribeVideo = {
        Type     = "Task"
        Resource = "arn:aws:states:::lambda:invoke"
        Arguments = {
          FunctionName = aws_lambda_function.functions["describe-video"].arn
          Payload = {
            jobId    = "{% $states.input.jobId %}"
            bucket   = "{% $states.input.bucket %}"
            videoKey = "{% $states.input.videoKey %}"
          }
        }
        Output = "{% $states.result.Payload %}" # { reportKey }
        Retry = concat([
          {
            # The model occasionally answers with unusable JSON; asking again usually fixes it.
            ErrorEquals     = ["ReportFormatException"]
            IntervalSeconds = 1
            MaxAttempts     = 2
          },
          {
            ErrorEquals     = ["ThrottlingException", "ServiceUnavailableException", "ModelNotReadyException", "ModelTimeoutException"]
            IntervalSeconds = 15
            MaxAttempts     = 4
            BackoffRate     = 2
            JitterStrategy  = "FULL"
          },
        ], local.lambda_retry)
        End = true
      }
    }
  }

  definition = {
    Comment        = "Analyze an uploaded security video with Amazon Nova and Amazon Rekognition"
    QueryLanguage  = "JSONata"
    TimeoutSeconds = var.timeout_seconds
    StartAt        = "Init"
    States = {
      Init = {
        Type = "Pass"
        Assign = {
          bucket = "{% $states.input.bucket %}"
          key    = "{% $states.input.key %}"
          jobId  = "{% $split($states.input.key, '/')[1] %}" # uploads/{jobId}/original.ext
          runId  = "{% $substring($replace($uuid(), '-', ''), 0, 12) %}"
        }
        Next = "StartJob"
      }
      StartJob = {
        Type     = "Task"
        Resource = "arn:aws:states:::lambda:invoke"
        Arguments = {
          FunctionName = aws_lambda_function.functions["start-job"].arn
          Payload      = { jobId = "{% $jobId %}", key = "{% $key %}" }
        }
        Output = "{% $states.result.Payload %}"
        Assign = { videoKey = "{% $states.result.Payload.videoKey %}" }
        Retry = concat([{
          # The upload event can arrive a moment before the API marks the job Uploaded.
          ErrorEquals     = ["JobNotReadyException"]
          IntervalSeconds = 3
          MaxAttempts     = 8
          BackoffRate     = 1.5
        }], local.lambda_retry)
        Catch = local.catch_all
        Next  = "ShouldAnalyze?"
      }
      "ShouldAnalyze?" = {
        Type    = "Choice"
        Choices = [{ Condition = "{% $states.input.skip %}", Next = "Skipped" }]
        Default = "Analyze"
      }
      Skipped = { Type = "Succeed" }
      Analyze = {
        Type      = "Parallel"
        Arguments = { jobId = "{% $jobId %}", runId = "{% $runId %}", bucket = "{% $bucket %}", videoKey = "{% $videoKey %}" }
        Branches  = concat(local.rekognition_branches, [local.describe_branch])
        Output    = "{% $merge($states.result) %}"
        Catch     = local.catch_all
        Next      = "CollectResults"
      }
      CollectResults = {
        Type     = "Task"
        Resource = "arn:aws:states:::lambda:invoke"
        Arguments = {
          FunctionName = aws_lambda_function.functions["collect-results"].arn
          Payload = {
            jobId      = "{% $jobId %}"
            labelJobId = "{% $states.input.labelsJobId %}"
            reportKey  = "{% $states.input.reportKey %}"
          }
        }
        Output = "{% $states.result.Payload %}"
        Retry  = local.lambda_retry
        Catch  = local.catch_all
        Next   = "Done"
      }
      Done = { Type = "Succeed" }
      MarkFailed = {
        Type     = "Task"
        Resource = "arn:aws:states:::lambda:invoke"
        Arguments = {
          FunctionName = aws_lambda_function.functions["fail-job"].arn
          Payload      = "{% $states.input %}"
        }
        Retry = local.lambda_retry
        Next  = "Failed"
      }
      Failed = {
        Type  = "Fail"
        Error = "AnalysisFailed"
        Cause = "The job record contains the error shown to the user."
      }
    }
  }
}

data "aws_iam_policy_document" "sfn_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["states.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "state_machine" {
  name               = "${local.prefix}-sfn-role"
  assume_role_policy = data.aws_iam_policy_document.sfn_assume.json
}

data "aws_iam_policy_document" "state_machine" {
  statement {
    sid       = "InvokeFunctions"
    actions   = ["lambda:InvokeFunction"]
    resources = flatten([for f in aws_lambda_function.functions : [f.arn, "${f.arn}:*"]])
  }

  statement {
    sid = "RunRekognitionVideo"
    actions = [
      "rekognition:StartLabelDetection",
      "rekognition:GetLabelDetection",
    ]
    resources = ["*"]
  }

  # Rekognition reads the video with the caller's (this role's) permissions.
  statement {
    sid       = "ReadUploadedVideos"
    actions   = ["s3:GetObject"]
    resources = ["${var.media_bucket_arn}/uploads/*"]
  }

  statement {
    sid = "DeliverLogs"
    actions = [
      "logs:CreateLogDelivery",
      "logs:GetLogDelivery",
      "logs:UpdateLogDelivery",
      "logs:DeleteLogDelivery",
      "logs:ListLogDeliveries",
      "logs:PutResourcePolicy",
      "logs:DescribeResourcePolicies",
      "logs:DescribeLogGroups",
    ]
    resources = ["*"]
  }
}

resource "aws_iam_role_policy" "state_machine" {
  name   = "analysis-state-machine"
  role   = aws_iam_role.state_machine.id
  policy = data.aws_iam_policy_document.state_machine.json
}

resource "aws_cloudwatch_log_group" "state_machine" {
  name              = "/aws/vendedlogs/states/${local.prefix}"
  retention_in_days = var.log_retention_days
}

resource "aws_sfn_state_machine" "analysis" {
  name       = local.prefix
  type       = "STANDARD"
  role_arn   = aws_iam_role.state_machine.arn
  definition = jsonencode(local.definition)

  logging_configuration {
    log_destination        = "${aws_cloudwatch_log_group.state_machine.arn}:*"
    include_execution_data = true
    level                  = "ERROR"
  }

  depends_on = [aws_iam_role_policy.state_machine]
}

# ---------- Trigger: new upload in S3 -> start an execution ----------

resource "aws_cloudwatch_event_rule" "upload_completed" {
  name        = "${local.prefix}-upload-completed"
  description = "A video finished uploading to the media bucket"
  event_pattern = jsonencode({
    source        = ["aws.s3"]
    "detail-type" = ["Object Created"]
    detail = {
      bucket = { name = [var.media_bucket_name] }
      object = { key = [{ prefix = "uploads/" }] }
    }
  })
}

data "aws_iam_policy_document" "events_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["events.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "events" {
  name               = "${local.prefix}-events-role"
  assume_role_policy = data.aws_iam_policy_document.events_assume.json
}

resource "aws_iam_role_policy" "events" {
  name = "start-analysis"
  role = aws_iam_role.events.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = "states:StartExecution"
      Resource = aws_sfn_state_machine.analysis.arn
    }]
  })
}

resource "aws_cloudwatch_event_target" "start_analysis" {
  rule     = aws_cloudwatch_event_rule.upload_completed.name
  arn      = aws_sfn_state_machine.analysis.arn
  role_arn = aws_iam_role.events.arn

  input_transformer {
    input_paths = {
      bucket = "$.detail.bucket.name"
      key    = "$.detail.object.key"
    }
    input_template = <<-EOT
      {"bucket": <bucket>, "key": <key>}
    EOT
  }
}
