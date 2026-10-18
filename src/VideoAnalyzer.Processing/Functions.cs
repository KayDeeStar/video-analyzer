using System.Text.Json;
using Amazon.BedrockRuntime;
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

public sealed record DescribeVideoInput(string JobId, string Bucket, string VideoKey);

public sealed record DescribeVideoOutput(string ReportKey);

public sealed record CollectResultsInput(string JobId, string LabelJobId, string? ReportKey);

public sealed record CollectResultsOutput(string JobId, int People, int Labels, int ReportPeople, int NotableEvents);

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
    private readonly NovaAnalyzer _nova;
    private readonly TimeProvider _clock;

    public Functions()
        : this(new AmazonS3Client(), new AmazonRekognitionClient(), new AmazonBedrockRuntimeClient(),
            Required("MEDIA_BUCKET"), Required("BEDROCK_MODEL_ID"), TimeProvider.System)
    {
    }

    public Functions(IAmazonS3 s3, IAmazonRekognition rekognition, IAmazonBedrockRuntime bedrock,
        string bucket, string modelId, TimeProvider clock)
    {
        _s3 = s3;
        _jobs = new JobStore(s3, bucket);
        _rekognition = new RekognitionReader(rekognition);
        _nova = new NovaAnalyzer(bedrock, modelId);
        _clock = clock;
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is not set.");

    private static string ReportKeyFor(string jobId) => $"work/{jobId}/report.json";

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

    /// <summary>Has the vision model describe the video and stores the report under work/.</summary>
    public async Task<DescribeVideoOutput> DescribeVideo(DescribeVideoInput input, ILambdaContext context)
    {
        using var cts = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(10));

        var (report, inputTokens, outputTokens) = await _nova.DescribeAsync(input.Bucket, input.VideoKey, cts.Token);
        context.Logger.LogInformation(
            $"Job {input.JobId}: {_nova.ModelId} used {inputTokens} input and {outputTokens} output tokens; " +
            $"{report.People.Count} people, {report.NotableEvents.Count} notable events.");

        var key = ReportKeyFor(input.JobId);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _jobs.Bucket,
            Key = key,
            ContentType = "application/json",
            ContentBody = JsonSerializer.Serialize(report, ApiJsonContext.Default.AiReport),
        }, cts.Token);
        return new DescribeVideoOutput(key);
    }

    /// <summary>Combines Rekognition labels and the AI report into result.json and marks the job Completed.</summary>
    public async Task<CollectResultsOutput> CollectResults(CollectResultsInput input, ILambdaContext context)
    {
        using var cts = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(10));
        var ct = cts.Token;

        var (video, labels) = await _rekognition.ReadLabelsAsync(input.LabelJobId, ct);
        var report = input.ReportKey is null ? null : await ReadReportAsync(input.ReportKey, ct);
        context.Logger.LogInformation($"Job {input.JobId}: {labels.Count} label detections, report: {report is not null}.");

        // Keep person positions for later steps (e.g. cropping portraits).
        var detections = new Detections(labels.Where(l => l.Name == ResultBuilder.PersonLabel).ToList());
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _jobs.Bucket,
            Key = $"work/{input.JobId}/detections.json",
            ContentType = "application/json",
            ContentBody = JsonSerializer.Serialize(detections, Json),
        }, ct);

        var result = ResultBuilder.Build(input.JobId, _clock.GetUtcNow(), video, labels, report, report is null ? null : _nova.ModelId);
        await _jobs.SaveResultAsync(result, ct);

        var job = await _jobs.GetAsync(input.JobId, ct)
            ?? throw new InvalidJobException($"Job {input.JobId} disappeared during analysis.");
        await _jobs.SaveAsync(job with { Status = JobStatus.Completed, Error = null, UpdatedAt = _clock.GetUtcNow() }, ct);

        return new CollectResultsOutput(input.JobId, result.People.MaxVisibleAtOnce, result.Labels.Count,
            report?.People.Count ?? 0, report?.NotableEvents.Count ?? 0);
    }

    private async Task<AiReport?> ReadReportAsync(string key, CancellationToken ct)
    {
        using var response = await _s3.GetObjectAsync(_jobs.Bucket, key, ct);
        return await JsonSerializer.DeserializeAsync(response.ResponseStream, ApiJsonContext.Default.AiReport, ct);
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
