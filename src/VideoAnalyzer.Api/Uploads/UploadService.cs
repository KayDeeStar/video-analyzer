using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using VideoAnalyzer.Core.Jobs;
using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Api.Uploads;

/// <summary>
/// Browser uploads go straight to S3 as a multipart upload with pre-signed part URLs,
/// so large videos never pass through API Gateway (10 MB / 30 s limits) or Lambda.
/// </summary>
public sealed class UploadService(
    IAmazonS3 s3,
    JobStore jobs,
    IOptions<StorageOptions> options,
    TimeProvider clock,
    ILogger<UploadService> logger)
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private readonly StorageOptions _options = options.Value;

    public async Task<CreateUploadResponse> StartAsync(CreateUploadRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var jobId = JobIds.New(now);
        var fileName = Path.GetFileName(request.FileName).Trim();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var videoKey = $"uploads/{jobId}/original{extension}";
        var contentType = ContentTypes.TryGetContentType(fileName, out var type) ? type : "application/octet-stream";

        var initiated = await s3.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = _options.MediaBucket,
            Key = videoKey,
            ContentType = contentType,
        }, ct);

        await jobs.SaveAsync(new JobRecord
        {
            JobId = jobId,
            FileName = fileName,
            FileSize = request.FileSize,
            ContentType = contentType,
            VideoKey = videoKey,
            UploadId = initiated.UploadId,
            Status = JobStatus.Uploading,
            CreatedAt = now,
            UpdatedAt = now,
        }, ct);

        var partSize = UploadValidator.PartSizeFor(request.FileSize, _options.PartSizeMb * 1024L * 1024L);
        var partCount = UploadValidator.PartCountFor(request.FileSize, partSize);
        var expiresAt = now.AddMinutes(_options.UploadUrlExpiryMinutes);

        var parts = new List<UploadPartUrl>(partCount);
        for (var partNumber = 1; partNumber <= partCount; partNumber++)
        {
            var url = await s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
            {
                BucketName = _options.MediaBucket,
                Key = videoKey,
                Verb = HttpVerb.PUT,
                UploadId = initiated.UploadId,
                PartNumber = partNumber,
                Expires = expiresAt.UtcDateTime,
            });
            parts.Add(new UploadPartUrl(partNumber, url));
        }

        logger.LogInformation("Started upload {JobId} ({FileName}, {FileSize} bytes, {PartCount} parts)",
            jobId, fileName, request.FileSize, partCount);

        return new CreateUploadResponse(jobId, partSize, parts, expiresAt);
    }

    public async Task<JobRecord> CompleteAsync(JobRecord job, CompleteUploadRequest request, CancellationToken ct)
    {
        await s3.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            BucketName = _options.MediaBucket,
            Key = job.VideoKey,
            UploadId = job.UploadId,
            PartETags = request.Parts
                .OrderBy(p => p.PartNumber)
                .Select(p => new PartETag(p.PartNumber, p.ETag))
                .ToList(),
        }, ct);

        // Part URLs cannot cap the bytes a client sends, so check the real object size.
        var head = await s3.GetObjectMetadataAsync(_options.MediaBucket, job.VideoKey, ct);
        if (head.ContentLength > _options.MaxUploadBytes)
        {
            await s3.DeleteObjectAsync(_options.MediaBucket, job.VideoKey, ct);
            return await SaveAsync(job with
            {
                Status = JobStatus.Failed,
                Error = $"Uploaded file exceeds the {_options.MaxUploadSizeMb} MB limit.",
                UploadId = null,
            }, ct);
        }

        logger.LogInformation("Completed upload {JobId} ({Bytes} bytes)", job.JobId, head.ContentLength);
        return await SaveAsync(job with { Status = JobStatus.Uploaded, FileSize = head.ContentLength, UploadId = null }, ct);
    }

    public async Task<JobRecord> CancelAsync(JobRecord job, CancellationToken ct)
    {
        await s3.AbortMultipartUploadAsync(_options.MediaBucket, job.VideoKey, job.UploadId, ct);
        return await SaveAsync(job with { Status = JobStatus.Failed, Error = "Upload cancelled.", UploadId = null }, ct);
    }

    public Task<string> GetVideoUrlAsync(JobRecord job) =>
        s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _options.MediaBucket,
            Key = job.VideoKey,
            Verb = HttpVerb.GET,
            Expires = clock.GetUtcNow().AddMinutes(_options.DownloadUrlExpiryMinutes).UtcDateTime,
        });

    private async Task<JobRecord> SaveAsync(JobRecord job, CancellationToken ct)
    {
        var updated = job with { UpdatedAt = clock.GetUtcNow() };
        await jobs.SaveAsync(updated, ct);
        return updated;
    }
}
