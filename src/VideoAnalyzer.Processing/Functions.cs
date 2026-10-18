using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Rekognition;
using Amazon.S3;
using Amazon.S3.Model;
using VideoAnalyzer.Core.Jobs;
using VideoAnalyzer.Shared;

[assembly: LambdaSerializer(typeof(CamelCaseLambdaJsonSerializer))]

namespace VideoAnalyzer.Processing;

public sealed record StartJobInput(string JobId, string Key);

/// <param name="Skip">True when this upload should not be analyzed (e.g. it was rejected or already analyzed).</param>
public sealed record StartJobOutput(string JobId, bool Skip, string VideoKey);

public sealed record CollectResultsInput(string JobId, string LabelJobId, string FaceJobId, string TextJobId);

public sealed record CollectResultsOutput(string JobId, int People, int Faces, int Labels, int Texts);

public sealed record FailJobInput(string JobId, string? Error, string? Cause);

/// <summary>The job record still says Uploading; Step Functions retries until the API has finished the upload.</summary>
public sealed class JobNotReadyException(string message) : Exception(message);

public sealed class UnsupportedVideoFormatException(string message) : Exception(message);

public sealed class InvalidJobException(string message) : Exception(message);

/// <summary>Lambda handlers called by the analysis state machine (see infra/modules/processing).</summary>
public sealed class Functions
{
    // Rekognition Video reads MP4 and MOV (H.264). Other formats get transcoded in a later step.
    private static readonly HashSet<string> AnalyzableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mov" };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IAmazonS3 _s3;
    private readonly JobStore _jobs;
    private readonly RekognitionReader _rekognition;
    private readonly TimeProvider _clock;

    public Functions()
        : this(new AmazonS3Client(), new AmazonRekognitionClient(),
            Environment.GetEnvironmentVariable("MEDIA_BUCKET") ?? throw new InvalidOperationException("MEDIA_BUCKET is not set."),
            TimeProvider.System)
    {
    }

    public Functions(IAmazonS3 s3, IAmazonRekognition rekognition, string bucket, TimeProvider clock)
    {
        _s3 = s3;
        _jobs = new JobStore(s3, bucket);
        _rekognition = new RekognitionReader(rekognition);
        _clock = clock;
    }

    /// <summary>Validates the uploaded video and marks the job Processing.</summary>
    public async Task<StartJobOutput> StartJob(StartJobInput input, ILambdaContext context)
    {
        if (!JobIds.IsValid(input.JobId))
            throw new InvalidJobException($"\"{input.Key}\" is not a job upload.");

        var job = await _jobs.GetAsync(input.JobId)
            ?? throw new InvalidJobException($"No job record exists for {input.Key}.");

        if (job.Status == JobStatus.Uploading)
            throw new JobNotReadyException($"Job {job.JobId} is still completing its upload.");

        if (job.Status != JobStatus.Uploaded || job.VideoKey != input.Key)
        {
            context.Logger.LogInformation($"Skipping {input.Key}: job {job.JobId} is {job.Status}.");
            return new StartJobOutput(job.JobId, Skip: true, job.VideoKey);
        }

        if (!AnalyzableExtensions.Contains(Path.GetExtension(job.VideoKey)))
            throw new UnsupportedVideoFormatException(
                $"{Path.GetExtension(job.FileName).ToUpperInvariant().TrimStart('.')} videos cannot be analyzed yet. " +
                "Please upload an MP4 or MOV file (support for other formats is coming).");

        await _jobs.SaveAsync(job with { Status = JobStatus.Processing, Error = null, UpdatedAt = _clock.GetUtcNow() });
        return new StartJobOutput(job.JobId, Skip: false, job.VideoKey);
    }

    /// <summary>Reads the finished Rekognition jobs, writes result.json and marks the job Completed.</summary>
    public async Task<CollectResultsOutput> CollectResults(CollectResultsInput input, ILambdaContext context)
    {
        using var cts = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(10));
        var ct = cts.Token;

        var labelsTask = _rekognition.ReadLabelsAsync(input.LabelJobId, ct);
        var facesTask = _rekognition.ReadFacesAsync(input.FaceJobId, ct);
        var textsTask = _rekognition.ReadTextAsync(input.TextJobId, ct);
        await Task.WhenAll(labelsTask, facesTask, textsTask);

        var (video, labels) = labelsTask.Result;
        var faces = facesTask.Result;
        var texts = textsTask.Result;
        context.Logger.LogInformation(
            $"Job {input.JobId}: {labels.Count} label, {faces.Count} face and {texts.Count} text detections.");

        // Keep the raw detections for later steps (frame extraction, AI descriptions).
        var detections = new Detections(labels.Where(l => l.Name == ResultBuilder.PersonLabel).ToList(), faces);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _jobs.Bucket,
            Key = $"work/{input.JobId}/detections.json",
            ContentType = "application/json",
            ContentBody = JsonSerializer.Serialize(detections, Json),
        }, ct);

        var result = ResultBuilder.Build(input.JobId, _clock.GetUtcNow(), video, labels, faces, texts);
        await _jobs.SaveResultAsync(result, ct);

        var job = await _jobs.GetAsync(input.JobId, ct)
            ?? throw new InvalidJobException($"Job {input.JobId} disappeared during analysis.");
        await _jobs.SaveAsync(job with { Status = JobStatus.Completed, Error = null, UpdatedAt = _clock.GetUtcNow() }, ct);

        return new CollectResultsOutput(input.JobId, result.People.MaxVisibleAtOnce, result.Faces.Count,
            result.Labels.Count, result.Texts.Count);
    }

    /// <summary>Marks the job Failed with a message a user can act on.</summary>
    public async Task FailJob(FailJobInput input, ILambdaContext context)
    {
        context.Logger.LogError($"Job {input.JobId} failed: {input.Error}: {input.Cause}");
        if (!JobIds.IsValid(input.JobId))
            return;

        var job = await _jobs.GetAsync(input.JobId);
        if (job is null)
            return;

        await _jobs.SaveAsync(job with
        {
            Status = JobStatus.Failed,
            Error = FailureMessages.Describe(input.Error, input.Cause),
            UpdatedAt = _clock.GetUtcNow(),
        });
    }
}
