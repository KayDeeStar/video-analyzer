variable "name_prefix" {
  description = "Prefix for resource names, e.g. videoanalyzer-dev."
  type        = string
}

variable "account_id" {
  description = "AWS account id, appended to bucket names to keep them globally unique."
  type        = string
}

variable "retention_days" {
  description = "Days to keep uploaded videos, job records and results before S3 deletes them."
  type        = number
  default     = 30
}

variable "work_retention_days" {
  description = "Days to keep intermediate processing files under work/."
  type        = number
  default     = 7
}

variable "cors_allowed_origins" {
  description = "Origins allowed to upload directly to the bucket from a browser."
  type        = list(string)
  default     = ["*"]
}

variable "force_destroy" {
  description = "Allow terraform destroy to delete the bucket even when it contains objects."
  type        = bool
  default     = false
}
