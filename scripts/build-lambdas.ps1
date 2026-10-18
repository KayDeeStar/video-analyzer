# Publishes the Lambda packages for linux-arm64 into artifacts/<name>.
# Terraform (infra/modules/api and infra/modules/processing) zips those folders and deploys them.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$projects = [ordered]@{
    'api'        = 'src/VideoAnalyzer.Api/VideoAnalyzer.Api.csproj'
    'processing' = 'src/VideoAnalyzer.Processing/VideoAnalyzer.Processing.csproj'
}

foreach ($name in $projects.Keys) {
    $out = Join-Path $root "artifacts/$name"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }

    dotnet publish (Join-Path $root $projects[$name]) `
        --configuration Release `
        --runtime linux-arm64 `
        --self-contained false `
        --output $out `
        -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $name failed ($LASTEXITCODE)" }
    Write-Host "Published $name to $out"
}
