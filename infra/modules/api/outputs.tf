output "api_url" {
  description = "Base URL of the HTTP API (append /api/...)."
  value       = aws_apigatewayv2_stage.default.invoke_url
}

output "api_endpoint" {
  description = "HTTP API endpoint without the stage path (used as a CloudFront origin)."
  value       = aws_apigatewayv2_api.api.api_endpoint
}

output "api_id" {
  value = aws_apigatewayv2_api.api.id
}

output "function_name" {
  value = aws_lambda_function.api.function_name
}
