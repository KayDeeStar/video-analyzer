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

variable "github_repository" {
  description = "GitHub repository (owner/name) whose Actions may deploy."
  type        = string
  default     = "KayDeeStar/video-analyzer"
}

variable "github_environment" {
  description = "GitHub environment the deploy job must run in (restricted to the main branch on GitHub)."
  type        = string
  default     = "dev"
}

variable "environments" {
  description = "Environments to generate backend.hcl files for (folders under infra/envs)."
  type        = list(string)
  default     = ["dev"]
}
