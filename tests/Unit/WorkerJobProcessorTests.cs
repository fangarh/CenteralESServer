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

    private sealed class RecordingQueue : IProcessingJobQueue
    {
        public CompleteProcessingJobCommand? Completed { get; private set; }
        public FailProcessingJobCommand? Failed { get; private set; }

        public Task<EnqueueProcessingJobResult> EnqueueAsync(CreateProcessingJobCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ClaimedProcessingJob?> ClaimNextAsync(DateTimeOffset now, CancellationToken cancellationToken)
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
