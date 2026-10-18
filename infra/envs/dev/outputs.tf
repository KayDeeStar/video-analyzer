output "media_bucket_name" {
  value = module.storage.media_bucket_name
}

output "media_bucket_arn" {
  value = module.storage.media_bucket_arn
}

output "api_url" {
  value = module.api.api_url
}

output "analysis_state_machine_arn" {
  value = module.processing.state_machine_arn
}

output "website_url" {
  value = module.frontend.url
}

output "web_bucket_name" {
  value = module.frontend.web_bucket_name
}

output "web_distribution_id" {
  value = module.frontend.distribution_id
}

output "api_function_name" {
  value = module.api.function_name
}
