using CenteralES.Processing;

namespace CenteralES.UnitTests;

public sealed class ProcessorErrorClassifierTests
{
    [Fact]
    public void Invalid_input_is_not_retryable_but_creates_failed_attempt()
    {
        var classification = ProcessorErrorClassifier.Classify(NormalizedProcessorError.InvalidInput);

        Assert.False(classification.IsRetryable);
        Assert.True(classification.CreatesFailedAttempt);
    }

    [Fact]
    public void Processor_overloaded_does_not_create_failed_attempt()
    {
        var classification = ProcessorErrorClassifier.Classify(NormalizedProcessorError.ProcessorOverloaded);

        Assert.False(classification.IsRetryable);
        Assert.False(classification.CreatesFailedAttempt);
    }

    [Fact]
    public void Contract_error_requires_admin_attention()
    {
        var classification = ProcessorErrorClassifier.Classify(NormalizedProcessorError.ProcessorContractError);

        Assert.True(classification.IsRetryable);
        Assert.True(classification.CreatesFailedAttempt);
        Assert.True(classification.RequiresAdminAttention);
    }
}
