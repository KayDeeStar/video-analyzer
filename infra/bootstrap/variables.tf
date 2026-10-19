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

variable "github_oidc_subject_prefix" {
  description = <<-EOT
    Start of the "sub" claim in GitHub's OIDC token for this repository. The repository uses
    immutable subjects (owner and repo names plus their numeric ids, so a renamed or re-created
    repo cannot take over the role). Check with: gh api repos/OWNER/REPO/actions/oidc/customization/sub
  EOT
  type        = string
  default     = "repo:KayDeeStar@83620889/video-analyzer@1424019897"
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
