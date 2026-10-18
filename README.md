# Video Analyzer

Upload security footage from a website; AWS analyzes it serverlessly (Rekognition Video + Amazon Bedrock)
and the same website shows people, descriptions, timestamps and other investigative details.

Stack: Blazor WebAssembly · C# (.NET) Lambda behind API Gateway · S3 · Step Functions · Terraform.

## Repository layout

```
infra/
  bootstrap/        one-time: S3 bucket for Terraform state
  modules/storage/  media bucket (videos, job status, results)
  envs/dev/         dev environment composition
src/                .NET solution (added in step 2)
```

## Prerequisites

- [Terraform](https://developer.hashicorp.com/terraform/install) >= 1.10
- [AWS CLI v2](https://aws.amazon.com/cli/) configured with credentials for the target account
- .NET SDK 10

## Deploy infrastructure

```powershell
# 1. One-time per account: Terraform state bucket (also writes infra/envs/dev/backend.hcl)
cd infra/bootstrap
terraform init
terraform apply

# 2. Dev environment
cd ../envs/dev
terraform init -backend-config=backend.hcl
terraform apply
```
