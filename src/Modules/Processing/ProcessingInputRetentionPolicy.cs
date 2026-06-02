namespace CenteralES.Processing;

public static class ProcessingInputRetentionPolicy
{
    public static bool ShouldDeleteTemporaryInputAfterTerminalState(ProcessingJobStatus status)
    {
        return status is ProcessingJobStatus.Completed;
    }
}
