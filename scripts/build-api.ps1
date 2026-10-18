# Publishes the API Lambda for linux-arm64 into artifacts/api.
# Terraform (infra/modules/api) zips that folder and deploys it.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts/api'

if (Test-Path $out) { Remove-Item -Recurse -Force $out }

dotnet publish (Join-Path $root 'src/VideoAnalyzer.Api/VideoAnalyzer.Api.csproj') `
    --configuration Release `
    --runtime linux-arm64 `
    --self-contained false `
    --output $out `
    -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Write-Host "API published to $out"
