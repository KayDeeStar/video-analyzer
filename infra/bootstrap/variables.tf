variable "region" {
  description = "AWS region for the Terraform state bucket."
  type        = string
  default     = "us-east-1"
}

variable "project" {
  description = "Project name, used as a prefix for resource names."
  type        = string
  default     = "videoanalyzer"
}

variable "environments" {
  description = "Environments to generate backend.hcl files for (folders under infra/envs)."
  type        = list(string)
  default     = ["dev"]
}
