using System.Net;
using System.Text.Json;
using Amazon.S3;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Microsoft.Extensions.Options;
using VideoAnalyzer.Core.Jobs;
using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Api.Jobs;

/// <summary>Bound from the "Analysis" config section (env var Analysis__StateMachineArn).</summary>
public sealed class AnalysisOptions
{
    public const string SectionName = "Analysis";

    /// <summary>The analysis state machine; re-analysis is unavailable when empty.</summary>
    public string StateMachineArn { get; set; } = "";
}

public enum ReanalyzeOutcome { Started, NotConfigured, NotFinished, VideoMissing }

/// <summary>Starts the analysis pipeline again for a video that is already uploaded.</summary>
public sealed class AnalysisService(
    IAmazonS3 s3,
    IAmazonStepFunctions stepFunctions,
    JobStore jobs,
    IOptions<AnalysisOptions> options,
    TimeProvider clock,
    ILogger<AnalysisService> logger)
{
    public async Task<(ReanalyzeOutcome Outcome, JobRecord Job)> ReanalyzeAsync(JobRecord job, CancellationToken ct)
    {
        var stateMachineArn = options.Value.StateMachineArn;
        if (string.IsNullOrEmpty(stateMachineArn))
            return (ReanalyzeOutcome.NotConfigured, job);
        if (job.Status is not (JobStatus.Completed or JobStatus.Failed))
            return (ReanalyzeOutcome.NotFinished, job);
        if (!await VideoExistsAsync(job, ct))
            return (ReanalyzeOutcome.VideoMissing, job);

        // The pipeline only analyzes jobs marked Uploaded (see the StartJob Lambda).
        var updated = job with { Status = JobStatus.Uploaded, Error = null, UpdatedAt = clock.GetUtcNow() };
        await jobs.SaveAsync(updated, ct);

        await stepFunctions.StartExecutionAsync(new StartExecutionRequest
        {
            StateMachineArn = stateMachineArn,
            Input = JsonSerializer.Serialize(new { bucket = jobs.Bucket, key = job.VideoKey }),
        }, ct);

        logger.LogInformation("Re-analysis started for {JobId}", job.JobId);
        return (ReanalyzeOutcome.Started, updated);
    }

    private async Task<bool> VideoExistsAsync(JobRecord job, CancellationToken ct)
    {
        try
        {
            await s3.GetObjectMetadataAsync(jobs.Bucket, job.VideoKey, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
