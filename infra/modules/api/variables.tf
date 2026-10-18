variable "name_prefix" {
  description = "Prefix for resource names, e.g. videoanalyzer-dev."
  type        = string
}

variable "package_dir" {
  description = "Folder containing the published API (output of scripts/build-api.ps1)."
  type        = string
}

variable "media_bucket_name" {
  type = string
}

variable "media_bucket_arn" {
  type = string
}

variable "max_upload_size_mb" {
  type    = number
  default = 2048
}

variable "cors_allowed_origins" {
  description = "Browser origins allowed to call the API directly (CloudFront makes it same-origin in step 3)."
  type        = list(string)
  default     = ["*"]
}

variable "throttling_rate_limit" {
  description = "Steady-state requests per second across the API."
  type        = number
  default     = 10
}

variable "throttling_burst_limit" {
  type    = number
  default = 20
}

variable "log_retention_days" {
  type    = number
  default = 14
}
