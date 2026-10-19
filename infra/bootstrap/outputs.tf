output "state_bucket_name" {
  description = "S3 bucket holding Terraform remote state."
  value       = aws_s3_bucket.state.id
}

output "github_deploy_role_name" {
  description = "Role GitHub Actions assumes (the workflow builds the ARN from the AWS_ACCOUNT_ID secret)."
  value       = aws_iam_role.github_deploy.name
}

output "account_id" {
  value = data.aws_caller_identity.current.account_id
}
