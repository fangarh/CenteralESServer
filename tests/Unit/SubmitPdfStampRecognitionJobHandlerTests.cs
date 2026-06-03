using CenteralES.PdfStampRecognition;
using CenteralES.Processing;
using CenteralES.Processing.Queue;
using CenteralES.Storage;

namespace CenteralES.UnitTests;

public sealed class SubmitPdfStampRecognitionJobHandlerTests
{
    [Fact]
    public async Task HandleAsync_returns_completed_result_when_hash_is_cached()
    {
        var resultStore = new RecordingResultStore();
        resultStore.CachedResult = new PdfStampRecognitionResult(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            await ComputeHashAsync("%PDF cached", ContentHashAlgorithm.Sha256),
            """{"source":"cache"}""",
            "test-v1",
            18,
            DateTimeOffset.UtcNow);
        var handler = CreateHandler(resultStore: resultStore);

        var result = await handler.HandleAsync(CreateCommand("%PDF cached"), CancellationToken.None);

        var completed = Assert.IsType<SubmitPdfStampRecognitionJobCompleted>(result);
        Assert.Equal(resultStore.CachedResult.ResultIndexId, completed.Result.ResultIndexId);
    }

    [Fact]
    public async Task HandleAsync_returns_temporary_storage_full_before_saving_or_enqueuing()
    {
        var commandQueue = new RecordingCommandQueue();
        var fileStore = new RecordingTemporaryFileStore();
        var handler = CreateHandler(
            commandQueue: commandQueue,
            temporaryFileStore: fileStore,
            temporaryStorageMonitor: new RecordingTemporaryStorageMonitor(TemporaryStorageCapacityStatus.Full));

        var result = await handler.HandleAsync(CreateCommand("%PDF full"), CancellationToken.None);

        Assert.IsType<SubmitPdfStampRecognitionJobTemporaryStorageFull>(result);
        Assert.False(fileStore.Saved);
        Assert.Null(commandQueue.Enqueued);
    }

    [Fact]
    public async Task HandleAsync_saves_temporary_input_and_enqueues_new_job()
    {
        var commandQueue = new RecordingCommandQueue();
        var fileStore = new RecordingTemporaryFileStore();
        var handler = CreateHandler(commandQueue: commandQueue, temporaryFileStore: fileStore);

        var result = await handler.HandleAsync(CreateCommand("%PDF new"), CancellationToken.None);

        var accepted = Assert.IsType<SubmitPdfStampRecognitionJobAccepted>(result);
        Assert.NotNull(commandQueue.Enqueued);
        Assert.True(fileStore.Saved);
        Assert.Equal(commandQueue.Enqueued.ContentHash, accepted.ContentHash);
        Assert.Equal(ProcessingJobStatus.Queued, accepted.Status);
        Assert.False(accepted.Deduplicated);
    }

    [Fact]
    public async Task HandleAsync_uses_requested_gost_hash_algorithm()
    {
        var commandQueue = new RecordingCommandQueue();
        var fileStore = new RecordingTemporaryFileStore();
        var handler = CreateHandler(commandQueue: commandQueue, temporaryFileStore: fileStore);

        var result = await handler.HandleAsync(
            CreateCommand("%PDF gost", ContentHashAlgorithm.GostR34112012_256),
            CancellationToken.None);

        var accepted = Assert.IsType<SubmitPdfStampRecognitionJobAccepted>(result);
        Assert.StartsWith($"{ContentHashAlgorithms.GostR34112012_256}:", accepted.ContentHash, StringComparison.Ordinal);
        Assert.Equal(commandQueue.Enqueued?.ContentHash, accepted.ContentHash);
        Assert.Contains(commandQueue.Enqueued!.ContentHashes!, hash => hash.Algorithm == ContentHashAlgorithms.Sha256);
        Assert.Contains(commandQueue.Enqueued.ContentHashes!, hash => hash.Algorithm == ContentHashAlgorithms.GostR34112012_256);
        Assert.StartsWith("incoming/gost-r-34-11-2012-256-", fileStore.LastKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_returns_cached_result_found_by_any_computed_hash()
    {
        var content = "%PDF cached alias";
        var commandQueue = new RecordingCommandQueue();
        var fileStore = new RecordingTemporaryFileStore();
        var resultStore = new RecordingResultStore
        {
            CachedResult = new PdfStampRecognitionResult(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                await ComputeHashAsync(content, ContentHashAlgorithm.Sha256),
                """{"source":"alias-cache"}""",
                "test-v1",
                24,
                DateTimeOffset.UtcNow)
        };
        var handler = CreateHandler(
            commandQueue: commandQueue,
            resultStore: resultStore,
            temporaryFileStore: fileStore);

        var result = await handler.HandleAsync(
            CreateCommand(content, ContentHashAlgorithm.GostR34112012_256),
            CancellationToken.None);

        var completed = Assert.IsType<SubmitPdfStampRecognitionJobCompleted>(result);
        Assert.Equal(resultStore.CachedResult.ResultIndexId, completed.Result.ResultIndexId);
        Assert.Null(commandQueue.Enqueued);
        Assert.False(fileStore.Saved);
        Assert.NotNull(commandQueue.RegisteredContentHashes);
        Assert.Equal(resultStore.CachedResult.SubjectId, commandQueue.RegisteredContentHashes.SubjectId);
        Assert.Contains(commandQueue.RegisteredContentHashes.ContentHashes, hash => hash.Algorithm == ContentHashAlgorithms.Sha256);
        Assert.Contains(commandQueue.RegisteredContentHashes.ContentHashes, hash => hash.Algorithm == ContentHashAlgorithms.GostR34112012_256);
    }

    private static SubmitPdfStampRecognitionJobCommand CreateCommand(
        string content,
        ContentHashAlgorithm algorithm = ContentHashAlgorithm.Sha256)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new SubmitPdfStampRecognitionJobCommand(
            () => new MemoryStream(bytes),
            bytes.Length,
            algorithm,
            DateTimeOffset.UtcNow);
    }

    private static async Task<string> ComputeHashAsync(string content, ContentHashAlgorithm algorithm)
    {
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        return await ContentHash.ComputeAsync(stream, algorithm, CancellationToken.None);
    }

    private static SubmitPdfStampRecognitionJobHandler CreateHandler(
        RecordingCommandQueue? commandQueue = null,
        RecordingReadStore? readStore = null,
        RecordingResultStore? resultStore = null,
        RecordingTemporaryFileStore? temporaryFileStore = null,
        RecordingTemporaryStorageMonitor? temporaryStorageMonitor = null)
    {
        return new SubmitPdfStampRecognitionJobHandler(
            commandQueue ?? new RecordingCommandQueue(),
            readStore ?? new RecordingReadStore(),
            resultStore ?? new RecordingResultStore(),
            new ContentHasher(),
            temporaryFileStore ?? new RecordingTemporaryFileStore(),
            temporaryStorageMonitor ?? new RecordingTemporaryStorageMonitor(TemporaryStorageCapacityStatus.Healthy));
    }

    private sealed class RecordingCommandQueue : IProcessingJobCommandQueue
    {
        public CreateProcessingJobCommand? Enqueued { get; private set; }
        public RegisterProcessingContentHashesCommand? RegisteredContentHashes { get; private set; }

        public Task<EnqueueProcessingJobResult> EnqueueAsync(CreateProcessingJobCommand command, CancellationToken cancellationToken)
        {
            Enqueued = command;
            return Task.FromResult(new EnqueueProcessingJobResult(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                ProcessingJobStatus.Queued,
                Deduplicated: false));
        }

        public Task RegisterContentHashesAsync(
            RegisterProcessingContentHashesCommand command,
            CancellationToken cancellationToken)
        {
            RegisteredContentHashes = command;
            return Task.CompletedTask;
        }

        public Task CompleteAsync(CompleteProcessingJobCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeferAsync(DeferProcessingJobCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task FailAsync(FailProcessingJobCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingReadStore : IProcessingJobReadStore
    {
        public ProcessingJobSnapshot? CurrentJob { get; init; }

        public Task<ProcessingJobSnapshot?> GetCurrentByHashAsync(string capability, string contentHash, CancellationToken cancellationToken)
        {
            return Task.FromResult(CurrentJob);
        }

        public Task<ProcessingJobSnapshot?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingResultStore : IPdfStampRecognitionResultStore
    {
        public PdfStampRecognitionResult? CachedResult { get; set; }

        public Task<PdfStampRecognitionResult> SaveAsync(SavePdfStampRecognitionResultCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PdfStampRecognitionResult?> GetByHashAsync(string contentHash, CancellationToken cancellationToken)
        {
            return Task.FromResult(CachedResult?.ContentHash == contentHash ? CachedResult : null);
        }
    }

    private sealed class RecordingTemporaryFileStore : ITemporaryFileStore
    {
        public bool Saved { get; private set; }
        public string? LastKey { get; private set; }

        public Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            Saved = true;
            LastKey = key;
            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteIfExistsAsync(string key, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingTemporaryStorageMonitor : ITemporaryStorageMonitor
    {
        private readonly TemporaryStorageCapacityStatus _status;

        public RecordingTemporaryStorageMonitor(TemporaryStorageCapacityStatus status)
        {
            _status = status;
        }

        public Task<TemporaryStorageCapacity> CheckCapacityAsync(
            TemporaryStorageCapacityRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new TemporaryStorageCapacity(
                _status,
                UsedBytes: 0,
                request.IncomingBytes,
                HardLimitBytes: null,
                SoftLimitBytes: null,
                AvailableFreeBytes: null,
                MinimumFreeBytes: null));
        }
    }
}
