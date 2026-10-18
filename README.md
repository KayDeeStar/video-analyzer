# Video Analyzer

Upload security footage from a website; AWS analyzes it serverlessly (Rekognition Video + Amazon Bedrock)
and the same website shows people, descriptions, timestamps and other investigative details.

Stack: Blazor WebAssembly · C# (.NET) Lambda behind API Gateway · S3 · Step Functions · Terraform.

## Repository layout

```
infra/
  bootstrap/        one-time: S3 bucket for Terraform state
  modules/storage/  media bucket (videos, job status, results)
  modules/api/      C# Lambda + API Gateway HTTP API
  envs/dev/         dev environment composition
src/
  VideoAnalyzer.Shared/  DTOs shared by the API and the Blazor UI
  VideoAnalyzer.Api/     ASP.NET Core Minimal API (runs in Lambda or locally)
tests/
  VideoAnalyzer.Api.Tests/
scripts/
  build-api.ps1     publish the API for Lambda (linux-arm64) into artifacts/api
  test-upload.ps1   end-to-end upload test against the deployed API
```

## API

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/health` | Liveness check |
| POST | `/api/uploads` | `{ fileName, fileSize }` → job id + pre-signed S3 part URLs |
| POST | `/api/uploads/{jobId}/complete` | `{ parts: [{ partNumber, eTag }] }` → finishes the upload |
| DELETE | `/api/uploads/{jobId}` | Cancel an in-progress upload |
| GET | `/api/jobs` | Recent jobs, newest first (`?limit=50`) |
| GET | `/api/jobs/{jobId}` | Job status, details and a short-lived video URL |

Videos never pass through API Gateway or Lambda: the browser PUTs each part directly to S3.

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

# 2. Build the API package (re-run before each apply that should ship code changes)
cd ../..
./scripts/build-api.ps1

# 3. Dev environment
cd infra/envs/dev
terraform init -backend-config=backend.hcl
terraform apply

# 4. Smoke test with any video file
cd ../../..
./scripts/test-upload.ps1 -File C:\path\to\video.mp4
```

## Run the API locally

Uses your local AWS credentials and the real dev bucket:

```powershell
$env:Storage__MediaBucket = (terraform -chdir=infra/envs/dev output -raw media_bucket_name)
dotnet run --project src/VideoAnalyzer.Api      # http://localhost:5080/api/health
dotnet test
```
