# Video Analyzer

Upload security footage from a website; AWS analyzes it serverlessly (Rekognition Video + Amazon Bedrock)
and the same website shows people, descriptions, timestamps and other investigative details.

Stack: Blazor WebAssembly · C# (.NET) Lambda behind API Gateway · S3 · Step Functions · Terraform.

## Repository layout

```
infra/
  bootstrap/          one-time: S3 bucket for Terraform state
  modules/storage/    media bucket (videos, job status, results)
  modules/api/        C# Lambda + API Gateway HTTP API
  modules/processing/ EventBridge -> Step Functions analysis pipeline + Lambdas
  modules/frontend/   web bucket + CloudFront (site and /api/* on one domain)
  envs/dev/           dev environment composition
src/
  VideoAnalyzer.Shared/      DTOs shared by the API, pipeline and Blazor UI
  VideoAnalyzer.Core/        job/result storage in S3 (used by API and pipeline)
  VideoAnalyzer.Api/         ASP.NET Core Minimal API (runs in Lambda or locally)
  VideoAnalyzer.Processing/  Lambda handlers called by the analysis state machine
  VideoAnalyzer.Web/         Blazor WebAssembly UI
tests/
  VideoAnalyzer.Api.Tests/
  VideoAnalyzer.Processing.Tests/
scripts/
  build-lambdas.ps1   publish the Lambdas (linux-arm64) into artifacts/api and artifacts/processing
  deploy-web.ps1      publish the Blazor site to S3/CloudFront
  test-upload.ps1     end-to-end upload test against the deployed API
```

## How analysis works

1. The browser uploads the video straight to S3 (`uploads/{jobId}/original.ext`).
2. S3 sends an "Object Created" event to EventBridge, which starts the Step Functions state machine.
3. The state machine marks the job `Processing`, runs the analyzers, and a Lambda writes
   `results/{jobId}/result.json` and marks the job `Completed` (or `Failed` with a readable reason).

## API

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/health` | Liveness check |
| POST | `/api/uploads` | `{ fileName, fileSize }` → job id + pre-signed S3 part URLs |
| POST | `/api/uploads/{jobId}/complete` | `{ parts: [{ partNumber, eTag }] }` → finishes the upload |
| DELETE | `/api/uploads/{jobId}` | Cancel an in-progress upload |
| GET | `/api/jobs` | Recent jobs, newest first (`?limit=50`) |
| GET | `/api/jobs/{jobId}` | Job status, details and a short-lived video URL |
| GET | `/api/jobs/{jobId}/results` | Analysis result (`AnalysisResult` JSON) once completed |

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
./scripts/build-lambdas.ps1

# 3. Dev environment
cd infra/envs/dev
terraform init -backend-config=backend.hcl
terraform apply

# 4. Smoke test with any video file
cd ../../..
./scripts/test-upload.ps1 -File C:\path\to\video.mp4
```

## CI/CD (GitHub Actions)

| Workflow | When | What |
|---|---|---|
| `ci.yml` | pull requests, pushes to other branches | build, test, `terraform fmt`/`validate` (no AWS access) |
| `deploy.yml` | pushes to `main`, manual run | the same checks, then build Lambdas, `terraform apply` the dev environment, optionally publish the site |

GitHub signs in to AWS with OpenID Connect; no AWS keys are stored in GitHub. `infra/bootstrap`
creates the OIDC provider and the `videoanalyzer-github-deploy` role, which only trusts this
repository's `dev` environment (Settings → Environments → dev, restricted to the `main` branch).

Environment `dev` settings:

- secret `AWS_ACCOUNT_ID`: kept secret so GitHub masks the account id in the public logs
- variable `TERRAFORM_TARGETS` (optional): e.g. `-target=module.api -target=module.processing`
- variable `DEPLOY_WEB` (optional): `true` to run `scripts/deploy-web.ps1` after apply

## Run the API locally

Uses your local AWS credentials and the real dev bucket:

```powershell
$env:Storage__MediaBucket = (terraform -chdir=infra/envs/dev output -raw media_bucket_name)
dotnet run --project src/VideoAnalyzer.Api      # http://localhost:5080/api/health
dotnet test
```
