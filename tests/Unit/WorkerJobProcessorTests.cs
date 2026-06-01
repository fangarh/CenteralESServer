using System.Text;
using CenteralES.PdfStampRecognition;
using CenteralES.Processing;
using CenteralES.Processing.Queue;
using CenteralES.Storage;
using CenteralES.Worker;
using Microsoft.Extensions.Logging.Abstractions;

namespace CenteralES.UnitTests;

public sealed class WorkerJobProcessorTests
{
    [Fact]
    public async Task ProcessAsync_reads_temporary_pdf_completes_job_and_deletes_file_after_success()
    {
        var job = new ClaimedProcessingJob(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            "incoming/test.pdf",
            1);
        var queue = new RecordingQueue();
        var recognizer = new RecordingRecognizer();
        var resultStore = new RecordingResultStore();
        var fileStore = new RecordingTemporaryFileStore("incoming/test.pdf", "%PDF test bytes");
        var processor = new WorkerJobProcessor(
            NullLogger<WorkerJobProcessor>.Instance,
            queue,
            recognizer,
            resultStore,
            fileStore);

        await processor.ProcessAsync(job, CancellationToken.None);

        Assert.Equal("%PDF test bytes", recognizer.ReadContent);
        Assert.Equal(job.TemporaryFileKey, fileStore.OpenedKey);
        Assert.Equal(job.TemporaryFileKey, fileStore.DeletedKey);
        Assert.NotNull(queue.Completed);
        Assert.Equal(job.JobId, queue.Completed.JobId);
        Assert.Equal(resultStore.ResultId, queue.Completed.ResultId);
        Assert.Null(queue.Failed);
    }

    [Fact]
    public async Task ProcessAsync_does_not_fail_completed_job_when_cleanup_fails()
    {
        var job = new ClaimedProcessingJob(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            "incoming/test.pdf",
            1);
        var queue = new RecordingQueue();
        var recognizer = new RecordingRecognizer();
        var resultStore = new RecordingResultStore();
        var fileStore = new RecordingTemporaryFileStore("incoming/test.pdf", "%PDF test bytes")
        {
            ThrowOnDelete = true
        };
        var processor = new WorkerJobProcessor(
            NullLogger<WorkerJobProcessor>.Instance,
            queue,
            recognizer,
            resultStore,
            fileStore);

        await processor.ProcessAsync(job, CancellationToken.None);

        Assert.NotNull(queue.Completed);
        Assert.Null(queue.Failed);
    }

    [Fact]
    public async Task ProcessAsync_preserves_normalized_adapter_error_diagnostics()
    {
        var job = new ClaimedProcessingJob(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            "incoming/test.pdf",
            1);
        var diagnostics = new AttemptDiagnostics(
            Endpoint: "https://pdf2txt.local/recognize_json/",
            Duration: TimeSpan.FromSeconds(1),
            HttpStatus: 504,
            NormalizedError: NormalizedProcessorError.ProcessorTimeout,
            Retryable: true,
            CorrelationId: "corr-1",
            RawErrorExcerpt: "Processor request timed out.");
        var queue = new RecordingQueue();
        var recognizer = new ThrowingRecognizer(new PdfStampRecognitionAdapterException(
            NormalizedProcessorError.ProcessorTimeout,
            diagnostics,
            "Processor request timed out."));
        var resultStore = new RecordingResultStore();
        var fileStore = new RecordingTemporaryFileStore("incoming/test.pdf", "%PDF test bytes");
        var processor = new WorkerJobProcessor(
            NullLogger<WorkerJobProcessor>.Instance,
            queue,
            recognizer,
            resultStore,
            fileStore);

        await processor.ProcessAsync(job, CancellationToken.None);

        Assert.Null(queue.Completed);
        Assert.NotNull(queue.Failed);
        Assert.Equal(NormalizedProcessorError.ProcessorTimeout, queue.Failed.Error);
        Assert.Equal("corr-1", queue.Failed.Diagnostics.CorrelationId);
        Assert.Equal("https://pdf2txt.local/recognize_json/", queue.Failed.Diagnostics.Endpoint);
    }

    [Fact]
    public async Task ProcessAsync_deletes_temporary_pdf_after_terminal_adapter_failure()
    {
        var job = new ClaimedProcessingJob(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            "incoming/test.pdf",
            1);
        var diagnostics = new AttemptDiagnostics(
            Endpoint: "https://pdf2txt.local/recognize_json/",
            Duration: TimeSpan.FromMilliseconds(20),
            HttpStatus: 400,
            NormalizedError: NormalizedProcessorError.InvalidInput,
            Retryable: false,
            CorrelationId: "corr-invalid",
            RawErrorExcerpt: "Processor rejected input.");
        var queue = new RecordingQueue();
        var recognizer = new ThrowingRecognizer(new PdfStampRecognitionAdapterException(
            NormalizedProcessorError.InvalidInput,
            diagnostics,
            "Processor rejected input."));
        var resultStore = new RecordingResultStore();
        var fileStore = new RecordingTemporaryFileStore("incoming/test.pdf", "%PDF invalid bytes");
        var processor = new WorkerJobProcessor(
            NullLogger<WorkerJobProcessor>.Instance,
            queue,
            recognizer,
            resultStore,
            fileStore);

        await processor.ProcessAsync(job, CancellationToken.None);

        Assert.NotNull(queue.Failed);
        Assert.True(queue.Failed.Final);
        Assert.Equal(job.TemporaryFileKey, fileStore.DeletedKey);
    }

    [Fact]
    public async Task ProcessAsync_marks_retryable_adapter_failure_terminal_at_max_attempts()
    {
        var job = new ClaimedProcessingJob(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            "incoming/test.pdf",
            5);
        var diagnostics = new AttemptDiagnostics(
            Endpoint: "https://pdf2txt.local/recognize_json/",
            Duration: TimeSpan.FromSeconds(30),
            HttpStatus: null,
            NormalizedError: NormalizedProcessorError.ProcessorTimeout,
            Retryable: true,
            CorrelationId: "corr-timeout-max",
            RawErrorExcerpt: "Processor request timed out.");
        var queue = new RecordingQueue();
        var recognizer = new ThrowingRecognizer(new PdfStampRecognitionAdapterException(
            NormalizedProcessorError.ProcessorTimeout,
            diagnostics,
            "Processor request timed out."));
        var resultStore = new RecordingResultStore();
        var fileStore = new RecordingTemporaryFileStore("incoming/test.pdf", "%PDF retry bytes");
        var processor = new WorkerJobProcessor(
            NullLogger<WorkerJobProcessor>.Instance,
            queue,
            recognizer,
            resultStore,
            fileStore);

        await processor.ProcessAsync(job, CancellationToken.None);

        Assert.NotNull(queue.Failed);
        Assert.True(queue.Failed.Final);
        Assert.Equal(job.TemporaryFileKey, fileStore.DeletedKey);
    }

    [Fact]
    public async Task ProcessAsync_marks_internal_worker_error_terminal_and_deletes_temporary_pdf()
    {
        var job = new ClaimedProcessingJob(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            "incoming/test.pdf",
            1);
        var queue = new RecordingQueue();
        var recognizer = new ThrowingRecognizer(new InvalidOperationException("unexpected recognizer failure"));
        var resultStore = new RecordingResultStore();
        var fileStore = new RecordingTemporaryFileStore("incoming/test.pdf", "%PDF internal error bytes");
        var processor = new WorkerJobProcessor(
            NullLogger<WorkerJobProcessor>.Instance,
            queue,
            recognizer,
            resultStore,
            fileStore);

        await processor.ProcessAsync(job, CancellationToken.None);

        Assert.NotNull(queue.Failed);
        Assert.True(queue.Failed.Final);
        Assert.Equal(NormalizedProcessorError.InternalError, queue.Failed.Error);
        Assert.False(queue.Failed.Diagnostics.Retryable);
        Assert.Equal(job.TemporaryFileKey, fileStore.DeletedKey);
    }

    [Fact]
    public async Task ProcessAsync_defers_job_without_failed_attempt_when_processor_is_overloaded()
    {
        var job = new ClaimedProcessingJob(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            "incoming/test.pdf",
            1);
        var diagnostics = new AttemptDiagnostics(
            Endpoint: null,
            Duration: TimeSpan.Zero,
            HttpStatus: null,
            NormalizedError: NormalizedProcessorError.ProcessorOverloaded,
            Retryable: false,
            CorrelationId: "corr-overloaded",
            RawErrorExcerpt: "Processor endpoint pool is saturated.");
        var queue = new RecordingQueue();
        var recognizer = new ThrowingRecognizer(new PdfStampRecognitionAdapterException(
            NormalizedProcessorError.ProcessorOverloaded,
            diagnostics,
            "Processor endpoint pool is saturated."));
        var resultStore = new RecordingResultStore();
        var fileStore = new RecordingTemporaryFileStore("incoming/test.pdf", "%PDF test bytes");
        var processor = new WorkerJobProcessor(
            NullLogger<WorkerJobProcessor>.Instance,
            queue,
            recognizer,
            resultStore,
            fileStore);

        await processor.ProcessAsync(job, CancellationToken.None);

        Assert.Null(queue.Completed);
        Assert.Null(queue.Failed);
        Assert.NotNull(queue.Deferred);
        Assert.Equal(job.JobId, queue.Deferred.JobId);
        Assert.True(queue.Deferred.ScheduledAt > queue.Deferred.DeferredAt);
        Assert.Null(fileStore.DeletedKey);
    }

    private sealed class RecordingQueue : IProcessingJobQueue
    {
        public CompleteProcessingJobCommand? Completed { get; private set; }
        public DeferProcessingJobCommand? Deferred { get; private set; }
        public FailProcessingJobCommand? Failed { get; private set; }

        public Task<EnqueueProcessingJobResult> EnqueueAsync(CreateProcessingJobCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ClaimedProcessingJob?> ClaimNextAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task RefreshHeartbeatAsync(RefreshProcessingJobHeartbeatCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ProcessingJobSnapshot?> GetCurrentByHashAsync(string capability, string contentHash, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ProcessingJobSnapshot?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task CompleteAsync(CompleteProcessingJobCommand command, CancellationToken cancellationToken)
        {
            Completed = command;
            return Task.CompletedTask;
        }

        public Task DeferAsync(DeferProcessingJobCommand command, CancellationToken cancellationToken)
        {
            Deferred = command;
            return Task.CompletedTask;
        }

        public Task FailAsync(FailProcessingJobCommand command, CancellationToken cancellationToken)
        {
            Failed = command;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRecognizer : IPdfStampRecognizer
    {
        public string? ReadContent { get; private set; }

        public async Task<PdfStampRecognitionAdapterResult> RecognizeAsync(
            ClaimedProcessingJob job,
            Stream pdfContent,
            CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(pdfContent, Encoding.UTF8);
            ReadContent = await reader.ReadToEndAsync(cancellationToken);

            return new PdfStampRecognitionAdapterResult(
                """{"source":"unit-test"}""",
                "unit-test-v1",
                new AttemptDiagnostics(
                    Endpoint: "fake://unit-test",
                    Duration: TimeSpan.FromMilliseconds(1),
                    HttpStatus: 200,
                    NormalizedError: null,
                    Retryable: null,
                    CorrelationId: Guid.NewGuid().ToString("N")));
        }
    }

    private sealed class ThrowingRecognizer : IPdfStampRecognizer
    {
        private readonly Exception _exception;

        public ThrowingRecognizer(Exception exception)
        {
            _exception = exception;
        }

        public Task<PdfStampRecognitionAdapterResult> RecognizeAsync(
            ClaimedProcessingJob job,
            Stream pdfContent,
            CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }

    private sealed class RecordingResultStore : IPdfStampRecognitionResultStore
    {
        public Guid ResultId { get; } = Guid.NewGuid();

        public Task<PdfStampRecognitionResult> SaveAsync(SavePdfStampRecognitionResultCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PdfStampRecognitionResult(
                ResultId,
                Guid.NewGuid(),
                command.SubjectId,
                command.JobId,
                command.ContentHash,
                command.PayloadJson,
                command.ContractVersion,
                Encoding.UTF8.GetByteCount(command.PayloadJson),
                command.CreatedAt));
        }

        public Task<PdfStampRecognitionResult?> GetByHashAsync(string contentHash, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingTemporaryFileStore : ITemporaryFileStore
    {
        private readonly string _key;
        private readonly byte[] _content;

        public RecordingTemporaryFileStore(string key, string content)
        {
            _key = key;
            _content = Encoding.UTF8.GetBytes(content);
        }

        public string? OpenedKey { get; private set; }
        public string? DeletedKey { get; private set; }
        public bool ThrowOnDelete { get; init; }

        public Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
        {
            Assert.Equal(_key, key);
            OpenedKey = key;
            return Task.FromResult<Stream>(new MemoryStream(_content));
        }

        public Task DeleteIfExistsAsync(string key, CancellationToken cancellationToken)
        {
            Assert.Equal(_key, key);
            if (ThrowOnDelete)
            {
                throw new IOException("cleanup failed");
            }

            DeletedKey = key;
            return Task.CompletedTask;
        }
    }
}
