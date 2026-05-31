namespace CenteralES.Processing;

public sealed record RetryClassification(
    bool IsRetryable,
    bool CreatesFailedAttempt,
    bool RequiresAdminAttention);

public static class ProcessorErrorClassifier
{
    public static RetryClassification Classify(NormalizedProcessorError error, bool internalErrorIsTransient = false)
    {
        return error switch
        {
            NormalizedProcessorError.InvalidInput => new(false, true, false),
            NormalizedProcessorError.ProcessorTimeout => new(true, true, false),
            NormalizedProcessorError.ProcessorUnreachable => new(true, true, false),
            NormalizedProcessorError.ProcessorHttpError => new(true, true, false),
            NormalizedProcessorError.ProcessorBadResponse => new(true, true, false),
            NormalizedProcessorError.ProcessorContractError => new(true, true, true),
            NormalizedProcessorError.ProcessorOverloaded => new(false, false, false),
            NormalizedProcessorError.TemporaryStorageFull => new(false, false, true),
            NormalizedProcessorError.InternalError => new(internalErrorIsTransient, true, !internalErrorIsTransient),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unknown processor error.")
        };
    }
}
