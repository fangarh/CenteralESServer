namespace CenteralES.Processing;

public sealed class ProcessingJob
{
    private ProcessingJob(
        Guid jobId,
        Guid subjectId,
        string capability,
        string contentHash,
        int attemptNumber,
        ProcessingJobStatus status,
        DateTimeOffset createdAt)
    {
        JobId = jobId;
        SubjectId = subjectId;
        Capability = EnsureNotBlank(capability, nameof(capability));
        ContentHash = EnsureNotBlank(contentHash, nameof(contentHash));
        AttemptNumber = attemptNumber > 0
            ? attemptNumber
            : throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be positive.");
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid JobId { get; }

    public Guid SubjectId { get; }

    public string Capability { get; }

    public string ContentHash { get; }

    public int AttemptNumber { get; }

    public ProcessingJobStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public AttemptDiagnostics? Diagnostics { get; private set; }

    public static ProcessingJob CreateQueued(
        Guid subjectId,
        string capability,
        string contentHash,
        int attemptNumber,
        DateTimeOffset createdAt)
    {
        return new ProcessingJob(Guid.NewGuid(), subjectId, capability, contentHash, attemptNumber, ProcessingJobStatus.Queued, createdAt);
    }

    public void Start(DateTimeOffset startedAt, string endpoint, string correlationId)
    {
        EnsureStatus(ProcessingJobStatus.Queued);

        Status = ProcessingJobStatus.Processing;
        StartedAt = startedAt;
        UpdatedAt = startedAt;
        Diagnostics = new AttemptDiagnostics(EnsureNotBlank(endpoint, nameof(endpoint)), null, null, null, null, EnsureNotBlank(correlationId, nameof(correlationId)));
    }

    public void Complete(DateTimeOffset finishedAt, TimeSpan duration)
    {
        EnsureStatus(ProcessingJobStatus.Processing);

        Status = ProcessingJobStatus.Completed;
        FinishedAt = finishedAt;
        UpdatedAt = finishedAt;
        Diagnostics = Diagnostics is null
            ? new AttemptDiagnostics(null, duration, null, null, null, Guid.NewGuid().ToString("N"))
            : Diagnostics with { Duration = duration };
    }

    public void Fail(DateTimeOffset finishedAt, NormalizedProcessorError error, bool final, TimeSpan duration, int? httpStatus = null, string? rawErrorExcerpt = null)
    {
        EnsureStatus(ProcessingJobStatus.Processing);

        var classification = ProcessorErrorClassifier.Classify(error);

        Status = final ? ProcessingJobStatus.Blocked : ProcessingJobStatus.Failed;
        FinishedAt = finishedAt;
        UpdatedAt = finishedAt;
        Diagnostics = (Diagnostics ?? new AttemptDiagnostics(null, null, null, null, null, Guid.NewGuid().ToString("N"))) with
        {
            Duration = duration,
            HttpStatus = httpStatus,
            NormalizedError = error,
            Retryable = classification.IsRetryable,
            RawErrorExcerpt = rawErrorExcerpt
        };
    }

    private void EnsureStatus(ProcessingJobStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Job status must be {expected}, but was {Status}.");
        }
    }

    private static string EnsureNotBlank(string value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be blank.", parameterName)
            : value;
    }
}
