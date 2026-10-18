variable "name_prefix" {
  description = "Prefix for resource names, e.g. videoanalyzer-dev."
  type        = string
}

variable "package_dir" {
  description = "Folder containing the published processing Lambdas (output of scripts/build-lambdas.ps1)."
  type        = string
}

variable "media_bucket_name" {
  type = string
}

variable "media_bucket_arn" {
  type = string
}

variable "poll_interval_seconds" {
  description = "How often to check whether the Rekognition jobs have finished."
  type        = number
  default     = 20
}

variable "timeout_seconds" {
  description = "Give up on an analysis after this long."
  type        = number
  default     = 14400 # 4 hours
}

variable "log_retention_days" {
  type    = number
  default = 14
}
