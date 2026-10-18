using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Web.Services;

/// <summary>Typed client for the Video Analyzer HTTP API.</summary>
public sealed class ApiClient(HttpClient http)
{
    private static ApiJsonContext Json => ApiJsonContext.Default;

    public async Task<CreateUploadResponse> StartUploadAsync(CreateUploadRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/uploads", request, Json.CreateUploadRequest, ct);
        return await ReadAsync(response, Json.CreateUploadResponse, ct);
    }

    public async Task<JobDetails> CompleteUploadAsync(string jobId, CompleteUploadRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync($"api/uploads/{Uri.EscapeDataString(jobId)}/complete",
            request, Json.CompleteUploadRequest, ct);
        return await ReadAsync(response, Json.JobDetails, ct);
    }

    public async Task CancelUploadAsync(string jobId, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/uploads/{Uri.EscapeDataString(jobId)}", ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
            throw new ApiException(await DescribeErrorAsync(response, ct));
    }

    public async Task<List<JobSummary>> GetJobsAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("api/jobs", ct);
        return await ReadAsync(response, Json.ListJobSummary, ct);
    }

    /// <summary>Returns null when the job does not exist.</summary>
    public async Task<JobDetails?> GetJobAsync(string jobId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"api/jobs/{Uri.EscapeDataString(jobId)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        return await ReadAsync(response, Json.JobDetails, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            throw new ApiException(await DescribeErrorAsync(response, ct));
        return await response.Content.ReadFromJsonAsync(type, ct)
            ?? throw new ApiException("The server returned an empty response.");
    }

    /// <summary>Turns ProblemDetails / validation errors into one readable message.</summary>
    private static async Task<string> DescribeErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var fallback = $"Request failed ({(int)response.StatusCode} {response.ReasonPhrase}).";
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return fallback;

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String)
                return root.GetString() ?? fallback;

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject()
                    .SelectMany(e => e.Value.EnumerateArray().Select(m => m.GetString()))
                    .OfType<string>();
                return string.Join(" ", messages);
            }

            if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } d)
                return d;
            if (root.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } t)
                return t;
        }
        catch (JsonException)
        {
        }
        return fallback;
    }
}

public sealed class ApiException(string message) : Exception(message);
