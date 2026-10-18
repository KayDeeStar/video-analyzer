variable "region" {
  type    = string
  default = "us-east-1"
}

variable "project" {
  type    = string
  default = "videoanalyzer"
}

variable "environment" {
  type    = string
  default = "dev"
}

variable "retention_days" {
  description = "Days to keep videos, job records and results."
  type        = number
  default     = 30
}

variable "max_upload_size_mb" {
  description = "Largest video the API will accept."
  type        = number
  default     = 2048
}

variable "bedrock_model_id" {
  description = "Bedrock vision model (or inference profile) used to describe people and scenes (step 7)."
  type        = string
  default     = "us.amazon.nova-lite-v1:0"
}

variable "local_dev_origins" {
  description = "Local Blazor dev server origins that may upload to the media bucket (besides the CloudFront site)."
  type        = list(string)
  default     = ["http://localhost:5100"]
}

variable "force_destroy" {
  description = "Let terraform destroy remove non-empty buckets. Convenient for dev; keep false for prod."
  type        = bool
  default     = true
}
