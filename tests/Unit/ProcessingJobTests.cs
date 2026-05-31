using CenteralES.Processing;

namespace CenteralES.UnitTests;

public sealed class ProcessingJobTests
{
    [Fact]
    public void Start_moves_queued_job_to_processing_and_records_endpoint()
    {
        var job = ProcessingJob.CreateQueued(Guid.NewGuid(), "pdf-stamp-recognition", "sha256:abc", 1, DateTimeOffset.UtcNow);

        job.Start(DateTimeOffset.UtcNow, "https://pdf2txt.local/recognize_json/", "corr-1");

        Assert.Equal(ProcessingJobStatus.Processing, job.Status);
        Assert.Equal("https://pdf2txt.local/recognize_json/", job.Diagnostics?.Endpoint);
        Assert.Equal("corr-1", job.Diagnostics?.CorrelationId);
    }

    [Fact]
    public void Complete_requires_processing_job()
    {
        var job = ProcessingJob.CreateQueued(Guid.NewGuid(), "pdf-stamp-recognition", "sha256:abc", 1, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => job.Complete(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Fail_records_normalized_error_and_retryability()
    {
        var job = ProcessingJob.CreateQueued(Guid.NewGuid(), "pdf-stamp-recognition", "sha256:abc", 1, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow, "https://pdf2txt.local/recognize_json/", "corr-1");

        job.Fail(DateTimeOffset.UtcNow, NormalizedProcessorError.ProcessorTimeout, final: false, TimeSpan.FromSeconds(15));

        Assert.Equal(ProcessingJobStatus.Failed, job.Status);
        Assert.Equal(NormalizedProcessorError.ProcessorTimeout, job.Diagnostics?.NormalizedError);
        Assert.True(job.Diagnostics?.Retryable);
    }

    [Fact]
    public void Final_failure_moves_job_to_blocked()
    {
        var job = ProcessingJob.CreateQueued(Guid.NewGuid(), "pdf-stamp-recognition", "sha256:abc", 5, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow, "https://pdf2txt.local/recognize_json/", "corr-1");

        job.Fail(DateTimeOffset.UtcNow, NormalizedProcessorError.ProcessorContractError, final: true, TimeSpan.FromSeconds(1), httpStatus: 200, rawErrorExcerpt: "unexpected json");

        Assert.Equal(ProcessingJobStatus.Blocked, job.Status);
        Assert.Equal("unexpected json", job.Diagnostics?.RawErrorExcerpt);
        Assert.Equal(200, job.Diagnostics?.HttpStatus);
    }
}
