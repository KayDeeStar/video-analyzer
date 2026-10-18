using System.Text.Json.Serialization;

namespace VideoAnalyzer.Shared;

[JsonConverter(typeof(JsonStringEnumConverter<JobStatus>))]
public enum JobStatus
{
    /// <summary>Job created; the browser is uploading the video parts.</summary>
    Uploading,

    /// <summary>Video is fully in S3 and waiting for analysis.</summary>
    Uploaded,

    /// <summary>The analysis pipeline is running.</summary>
    Processing,

    /// <summary>Analysis finished; results are available.</summary>
    Completed,

    /// <summary>Upload or analysis failed (see the job's Error).</summary>
    Failed,
}
