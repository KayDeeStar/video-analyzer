using Amazon.S3;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using VideoAnalyzer.Api.Jobs;
using VideoAnalyzer.Core.Jobs;
using VideoAnalyzer.Api.Uploads;
using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Api;

public static class Endpoints
{
    public static void MapVideoAnalyzerApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }));

        var uploads = api.MapGroup("/uploads");
        uploads.MapPost("/", CreateUpload);
        uploads.MapPost("/{jobId}/complete", CompleteUpload);
        uploads.MapDelete("/{jobId}", CancelUpload);

        var jobs = api.MapGroup("/jobs");
        jobs.MapGet("/", ListJobs);
        jobs.MapGet("/{jobId}", GetJob);
        jobs.MapGet("/{jobId}/results", GetResults);
        jobs.MapPost("/{jobId}/reanalyze", Reanalyze);
    }

    private static async Task<Results<Ok<JobDetails>, NotFound, Conflict<string>, ProblemHttpResult>> Reanalyze(
        string jobId, JobStore jobs, AnalysisService analysis, CancellationToken ct)
    {
        var job = JobIds.IsValid(jobId) ? await jobs.GetAsync(jobId, ct) : null;
        if (job is null)
            return TypedResults.NotFound();

        var (outcome, updated) = await analysis.ReanalyzeAsync(job, ct);
        return outcome switch
        {
            ReanalyzeOutcome.Started => TypedResults.Ok(updated.ToDetails(videoUrl: null)),
            ReanalyzeOutcome.NotFinished => TypedResults.Conflict($"Job is {job.Status}; wait until it has finished."),
            ReanalyzeOutcome.VideoMissing => TypedResults.Conflict("The video is no longer available."),
            _ => TypedResults.Problem("Re-analysis is not configured on this server.", statusCode: 503),
        };
    }

    private static async Task<Results<Ok<CreateUploadResponse>, ValidationProblem>> CreateUpload(
        CreateUploadRequest request, UploadService uploads, IOptions<StorageOptions> options, CancellationToken ct)
    {
        var errors = UploadValidator.Validate(request, options.Value.MaxUploadBytes);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        return TypedResults.Ok(await uploads.StartAsync(request, ct));
    }

    private static async Task<Results<Ok<JobDetails>, NotFound, ValidationProblem, Conflict<string>>> CompleteUpload(
        string jobId, CompleteUploadRequest request, JobStore jobs, UploadService uploads, CancellationToken ct)
    {
        if (!JobIds.IsValid(jobId))
            return TypedResults.NotFound();
        if (request.Parts is not { Count: > 0 } || request.Parts.Any(p => p.PartNumber < 1 || string.IsNullOrWhiteSpace(p.ETag)))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Parts)] = ["Each uploaded part needs a part number and ETag."],
            });

        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
            return TypedResults.NotFound();
        if (job.Status != JobStatus.Uploading)
            return TypedResults.Conflict($"Job is {job.Status}, not Uploading.");

        try
        {
            job = await uploads.CompleteAsync(job, request, ct);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "InvalidPart" or "InvalidPartOrder" or "EntityTooSmall" or "NoSuchUpload")
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Parts)] = [$"S3 rejected the upload: {ex.Message}"],
            });
        }

        return TypedResults.Ok(job.ToDetails(videoUrl: null));
    }

    private static async Task<Results<NoContent, NotFound, Conflict<string>>> CancelUpload(
        string jobId, JobStore jobs, UploadService uploads, CancellationToken ct)
    {
        var job = JobIds.IsValid(jobId) ? await jobs.GetAsync(jobId, ct) : null;
        if (job is null)
            return TypedResults.NotFound();
        if (job.Status != JobStatus.Uploading)
            return TypedResults.Conflict($"Job is {job.Status}, not Uploading.");

        await uploads.CancelAsync(job, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<IEnumerable<JobSummary>>> ListJobs(JobStore jobs, CancellationToken ct, int limit = 50)
    {
        var recent = await jobs.ListRecentAsync(Math.Clamp(limit, 1, 200), ct);
        return TypedResults.Ok(recent.Select(j => j.ToSummary()));
    }

    private static async Task<Results<Ok<JobDetails>, NotFound>> GetJob(
        string jobId, JobStore jobs, UploadService uploads, CancellationToken ct)
    {
        var job = JobIds.IsValid(jobId) ? await jobs.GetAsync(jobId, ct) : null;
        if (job is null)
            return TypedResults.NotFound();

        var hasVideo = job.Status is JobStatus.Uploaded or JobStatus.Processing or JobStatus.Completed;
        var videoUrl = hasVideo ? await uploads.GetVideoUrlAsync(job) : null;
        return TypedResults.Ok(job.ToDetails(videoUrl));
    }

    /// <summary>Streams results/{jobId}/result.json (an <see cref="AnalysisResult"/>) once analysis has completed.</summary>
    private static async Task<Results<FileStreamHttpResult, NotFound>> GetResults(
        string jobId, JobStore jobs, CancellationToken ct)
    {
        var result = JobIds.IsValid(jobId) ? await jobs.OpenResultAsync(jobId, ct) : null;
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(result, "application/json");
    }
}
