# Publishes the Blazor WebAssembly site and uploads it to the web bucket behind CloudFront.
# Run after `terraform apply` in infra/envs/dev (reads the bucket and distribution from its outputs).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts/web'
$site = Join-Path $out 'wwwroot'

Push-Location (Join-Path $root 'infra/envs/dev')
try {
    $bucket = terraform output -raw web_bucket_name
    $distribution = terraform output -raw web_distribution_id
    $url = terraform output -raw website_url
}
finally { Pop-Location }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root 'src/VideoAnalyzer.Web/VideoAnalyzer.Web.csproj') --configuration Release --output $out -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

function Invoke-Aws {
    aws @args
    if ($LASTEXITCODE -ne 0) { throw "aws $($args[0]) $($args[1]) failed ($LASTEXITCODE)" }
}

# 1. Upload everything, removing files from older deployments. Pre-compressed .br/.gz copies are
#    skipped: S3 cannot pick between them per browser; CloudFront compresses on the fly instead.
Invoke-Aws s3 sync $site "s3://$bucket" --delete --exclude '*.br' --exclude '*.gz' `
    --cache-control 'public,max-age=86400' --only-show-errors

# 2. Re-upload key file types with explicit content types (Windows' MIME registry is unreliable,
#    and browsers only stream-compile WebAssembly served as application/wasm).
$types = [ordered]@{
    '.wasm' = 'application/wasm'
    '.js'   = 'text/javascript'
    '.mjs'  = 'text/javascript'
    '.json' = 'application/json'
    '.css'  = 'text/css'
    '.dat'  = 'application/octet-stream'
}
foreach ($ext in $types.Keys) {
    Invoke-Aws s3 cp $site "s3://$bucket" --recursive --exclude '*' --include "*$ext" `
        --content-type $types[$ext] --cache-control 'public,max-age=86400' --only-show-errors
}

# 3. index.html last and never cached, so browsers always pick up the newest build.
Invoke-Aws s3 cp (Join-Path $site 'index.html') "s3://$bucket/index.html" `
    --content-type 'text/html; charset=utf-8' --cache-control 'no-cache' --only-show-errors

# 4. Drop CloudFront's cached copies.
Invoke-Aws cloudfront create-invalidation --distribution-id $distribution --paths '/*' --query 'Invalidation.Id' --output text | Out-Null

Write-Host "Deployed to $url"
