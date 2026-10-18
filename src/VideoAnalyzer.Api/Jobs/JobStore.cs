using System.Net;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace VideoAnalyzer.Api.Jobs;

/// <summary>Reads and writes job records as JSON objects under jobs/ in the media bucket.</summary>
public sealed class JobStore(IAmazonS3 s3, IOptions<StorageOptions> options)
{
    private const string Prefix = "jobs/";
    private const string FileName = "job.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _bucket = options.Value.MediaBucket;

    public static string KeyFor(string jobId) => $"{Prefix}{jobId}/{FileName}";

    public async Task SaveAsync(JobRecord job, CancellationToken ct)
    {
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = KeyFor(job.JobId),
            ContentType = "application/json",
            ContentBody = JsonSerializer.Serialize(job, Json),
        }, ct);
    }

    public async Task<JobRecord?> GetAsync(string jobId, CancellationToken ct)
    {
        try
        {
            using var response = await s3.GetObjectAsync(_bucket, KeyFor(jobId), ct);
            return await JsonSerializer.DeserializeAsync<JobRecord>(response.ResponseStream, Json, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Most recent jobs first.</summary>
    public async Task<IReadOnlyList<JobRecord>> ListRecentAsync(int limit, CancellationToken ct)
    {
        var keys = new List<string>();
        var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = Prefix };
        ListObjectsV2Response page;
        do
        {
            page = await s3.ListObjectsV2Async(request, ct);
            keys.AddRange((page.S3Objects ?? []).Select(o => o.Key).Where(k => k.EndsWith("/" + FileName)));
            request.ContinuationToken = page.NextContinuationToken;
        } while (page.IsTruncated == true);

        // Job ids start with a UTC timestamp, so descending key order is newest first.
        var newest = keys.OrderDescending(StringComparer.Ordinal).Take(limit)
            .Select(k => k[Prefix.Length..^(FileName.Length + 1)]);

        var jobs = await Task.WhenAll(newest.Select(id => GetAsync(id, ct)));
        return jobs.OfType<JobRecord>().ToList();
    }
}
