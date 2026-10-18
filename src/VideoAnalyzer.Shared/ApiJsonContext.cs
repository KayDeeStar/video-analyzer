using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoAnalyzer.Shared;

/// <summary>
/// Source-generated JSON metadata for the API contracts. Lets the trimmed Blazor
/// WebAssembly app (de)serialize them without reflection.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CreateUploadRequest))]
[JsonSerializable(typeof(CreateUploadResponse))]
[JsonSerializable(typeof(CompleteUploadRequest))]
[JsonSerializable(typeof(JobDetails))]
[JsonSerializable(typeof(List<JobSummary>))]
[JsonSerializable(typeof(AnalysisResult))]
[JsonSerializable(typeof(AiReport))]
public partial class ApiJsonContext : JsonSerializerContext;
