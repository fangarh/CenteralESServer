namespace CenteralES.Processing;

public static class ProcessingJobStatusMapper
{
    public static string ToDatabaseValue(this ProcessingJobStatus status)
    {
        return status switch
        {
            ProcessingJobStatus.Queued => "queued",
            ProcessingJobStatus.Processing => "processing",
            ProcessingJobStatus.Completed => "completed",
            ProcessingJobStatus.Failed => "failed",
            ProcessingJobStatus.Blocked => "blocked",
            ProcessingJobStatus.Cancelled => "cancelled",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown processing job status.")
        };
    }

    public static ProcessingJobStatus Parse(string status)
    {
        return TryParse(status, out var parsed)
            ? parsed!.Value
            : throw new InvalidOperationException($"Unknown job status '{status}'.");
    }

    public static bool TryParse(string status, out ProcessingJobStatus? parsed)
    {
        parsed = status.ToLowerInvariant() switch
        {
            "queued" => ProcessingJobStatus.Queued,
            "processing" => ProcessingJobStatus.Processing,
            "completed" => ProcessingJobStatus.Completed,
            "failed" => ProcessingJobStatus.Failed,
            "blocked" => ProcessingJobStatus.Blocked,
            "cancelled" => ProcessingJobStatus.Cancelled,
            _ => null
        };

        return parsed is not null;
    }
}
