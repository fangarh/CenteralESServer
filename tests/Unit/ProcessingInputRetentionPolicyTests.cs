using CenteralES.Processing;

namespace CenteralES.UnitTests;

public sealed class ProcessingInputRetentionPolicyTests
{
    [Fact]
    public void Completed_jobs_delete_temporary_input()
    {
        Assert.True(ProcessingInputRetentionPolicy.ShouldDeleteTemporaryInputAfterTerminalState(ProcessingJobStatus.Completed));
    }

    [Theory]
    [InlineData(ProcessingJobStatus.Failed)]
    [InlineData(ProcessingJobStatus.Blocked)]
    [InlineData(ProcessingJobStatus.Cancelled)]
    public void Failed_or_operator_actionable_terminal_jobs_preserve_temporary_input(ProcessingJobStatus status)
    {
        Assert.False(ProcessingInputRetentionPolicy.ShouldDeleteTemporaryInputAfterTerminalState(status));
    }
}
