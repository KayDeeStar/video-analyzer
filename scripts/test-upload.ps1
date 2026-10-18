# End-to-end API test: uploads a video exactly like the browser will (multipart, pre-signed URLs).
# Usage:  .\scripts\test-upload.ps1 -File C:\path\video.mp4 [-ApiUrl https://xxxx.execute-api.us-east-1.amazonaws.com/]
# If -ApiUrl is omitted, it is read from the dev Terraform outputs.
param(
    [Parameter(Mandatory)] [string] $File,
    [string] $ApiUrl
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

if (-not $ApiUrl) {
    Push-Location (Join-Path $PSScriptRoot '../infra/envs/dev')
    try { $ApiUrl = terraform output -raw api_url } finally { Pop-Location }
}
$ApiUrl = $ApiUrl.TrimEnd('/')
$info = Get-Item $File

# 1. Start the upload: the API creates the job and returns one pre-signed URL per part.
$body = @{ fileName = $info.Name; fileSize = $info.Length } | ConvertTo-Json
$upload = Invoke-RestMethod -Method Post -Uri "$ApiUrl/api/uploads" -ContentType 'application/json' -Body $body
Write-Host "Job $($upload.jobId): $($upload.parts.Count) part(s) of up to $($upload.partSize) bytes"

# 2. PUT each byte range straight to S3 and remember the ETag S3 returns.
$http = New-Object System.Net.Http.HttpClient
$http.Timeout = [TimeSpan]::FromMinutes(30)
$stream = [System.IO.File]::OpenRead($info.FullName)
$completed = @()
try {
    foreach ($part in $upload.parts) {
        $offset = ([long]$part.partNumber - 1) * [long]$upload.partSize
        $length = [int][Math]::Min([long]$upload.partSize, $info.Length - $offset)
        $buffer = New-Object byte[] $length
        $stream.Position = $offset
        $read = 0
        while ($read -lt $length) { $read += $stream.Read($buffer, $read, $length - $read) }

        $response = $http.PutAsync($part.url, (New-Object System.Net.Http.ByteArrayContent(, $buffer))).Result
        if (-not $response.IsSuccessStatusCode) {
            throw "Part $($part.partNumber) failed: $([int]$response.StatusCode) $($response.Content.ReadAsStringAsync().Result)"
        }
        $etag = ($response.Headers.GetValues('ETag') | Select-Object -First 1)
        $completed += @{ partNumber = $part.partNumber; eTag = $etag }
        Write-Host "  part $($part.partNumber)/$($upload.parts.Count) uploaded"
    }
}
finally {
    $stream.Dispose()
    $http.Dispose()
}

# 3. Complete the upload, then read the job back.
$done = Invoke-RestMethod -Method Post -Uri "$ApiUrl/api/uploads/$($upload.jobId)/complete" `
    -ContentType 'application/json' -Body (@{ parts = $completed } | ConvertTo-Json -Depth 3)
Write-Host "Status after complete: $($done.status)"

$job = Invoke-RestMethod -Uri "$ApiUrl/api/jobs/$($upload.jobId)"
$job | Select-Object jobId, fileName, fileSize, status, createdAt | Format-List
Write-Host "Video URL (valid ~15 min): $($job.videoUrl.Substring(0, [Math]::Min(90, $job.videoUrl.Length)))..."
