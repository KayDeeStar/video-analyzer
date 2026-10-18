output "state_bucket_name" {
  description = "S3 bucket holding Terraform remote state."
  value       = aws_s3_bucket.state.id
}

output "account_id" {
  value = data.aws_caller_identity.current.account_id
}
