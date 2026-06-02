using CenteralES.Processing;
using CenteralES.Processing.Queue;
using Npgsql;

namespace CenteralES.Infrastructure.Processing;

public sealed class PostgresProcessingJobQueue : IProcessingJobQueue
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresProcessingJobQueue(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<EnqueueProcessingJobResult> EnqueueAsync(CreateProcessingJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existing = await FindSubjectAsync(connection, transaction, command, cancellationToken);
        if (existing is not null && IsActive(existing.CurrentJobStatus))
        {
            await UpsertContentHashesAsync(connection, transaction, existing.SubjectId, command, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new EnqueueProcessingJobResult(
                existing.SubjectId,
                existing.CurrentJobId!.Value,
                existing.CurrentAttemptNumber!.Value,
                ProcessingJobStatusMapper.Parse(existing.CurrentJobStatus!),
                Deduplicated: true);
        }

        var now = command.CreatedAt;
        var subjectId = existing?.SubjectId ?? Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var attemptNumber = existing is null
            ? 1
            : await GetNextAttemptNumberAsync(connection, transaction, subjectId, cancellationToken);

        if (existing is null)
        {
            await using var insertSubject = new NpgsqlCommand("""
                insert into processing_subjects (
                    id,
                    capability,
                    content_hash,
                    current_job_id,
                    state,
                    result_id,
                    created_at,
                    updated_at)
                values (
                    @id,
                    @capability,
                    @content_hash,
                    @current_job_id,
                    'queued',
                    null,
                    @created_at,
                    @updated_at);
                """, connection, transaction);
            insertSubject.Parameters.AddWithValue("id", subjectId);
            insertSubject.Parameters.AddWithValue("capability", command.Capability);
            insertSubject.Parameters.AddWithValue("content_hash", command.ContentHash);
            insertSubject.Parameters.AddWithValue("current_job_id", jobId);
            insertSubject.Parameters.AddWithValue("created_at", now);
            insertSubject.Parameters.AddWithValue("updated_at", now);
            await insertSubject.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpsertContentHashesAsync(connection, transaction, subjectId, command, cancellationToken);

        await using var insertJob = new NpgsqlCommand("""
            insert into processing_jobs (
                id,
                subject_id,
                capability,
                content_hash,
                temporary_file_key,
                attempt_number,
                status,
                scheduled_at,
                created_at,
                updated_at)
            values (
                @id,
                @subject_id,
                @capability,
                @content_hash,
                @temporary_file_key,
                @attempt_number,
                'queued',
                @scheduled_at,
                @created_at,
                @updated_at);
            """, connection, transaction);
        insertJob.Parameters.AddWithValue("id", jobId);
        insertJob.Parameters.AddWithValue("subject_id", subjectId);
        insertJob.Parameters.AddWithValue("capability", command.Capability);
        insertJob.Parameters.AddWithValue("content_hash", command.ContentHash);
        insertJob.Parameters.AddWithValue("temporary_file_key", command.TemporaryFileKey);
        insertJob.Parameters.AddWithValue("attempt_number", attemptNumber);
        insertJob.Parameters.AddWithValue("scheduled_at", now);
        insertJob.Parameters.AddWithValue("created_at", now);
        insertJob.Parameters.AddWithValue("updated_at", now);
        await insertJob.ExecuteNonQueryAsync(cancellationToken);

        if (existing is not null)
        {
            await using var updateSubject = new NpgsqlCommand("""
                update processing_subjects
                set current_job_id = @current_job_id,
                    state = 'queued',
                    updated_at = @updated_at
                where id = @id;
                """, connection, transaction);
            updateSubject.Parameters.AddWithValue("current_job_id", jobId);
            updateSubject.Parameters.AddWithValue("updated_at", now);
            updateSubject.Parameters.AddWithValue("id", subjectId);
            await updateSubject.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new EnqueueProcessingJobResult(subjectId, jobId, attemptNumber, ProcessingJobStatus.Queued, Deduplicated: false);
    }

    public async Task<ClaimedProcessingJob?> ClaimNextAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(PostgresProcessingSql.ClaimNext, connection, transaction);
        command.Parameters.AddWithValue("now", now);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var claimed = new ClaimedProcessingJob(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5));

        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);

        return claimed;
    }

    public async Task RefreshHeartbeatAsync(RefreshProcessingJobHeartbeatCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var update = new NpgsqlCommand("""
            update processing_jobs
            set heartbeat_at = @heartbeat_at,
                updated_at = @heartbeat_at
            where id = @job_id
              and status = 'processing';
            """, connection);
        update.Parameters.AddWithValue("heartbeat_at", command.HeartbeatAt);
        update.Parameters.AddWithValue("job_id", command.JobId);

        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> RecoverStaleProcessingJobsAsync(RecoverStaleProcessingJobsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Capability))
        {
            throw new ArgumentException("Capability is required.", nameof(command));
        }

        var limit = Math.Clamp(command.Limit, 1, 500);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var recover = new NpgsqlCommand(PostgresProcessingSql.RecoverStaleProcessingJobs, connection, transaction);
        recover.Parameters.AddWithValue("capability", command.Capability);
        recover.Parameters.AddWithValue("stale_before", command.StaleBefore);
        recover.Parameters.AddWithValue("recovered_at", command.RecoveredAt);
        recover.Parameters.AddWithValue("limit", limit);

        var recovered = Convert.ToInt32(await recover.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return recovered;
    }

    public async Task<ProcessingJobSnapshot?> GetCurrentByHashAsync(string capability, string contentHash, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                j.subject_id,
                j.id,
                j.capability,
                j.content_hash,
                j.temporary_file_key,
                j.attempt_number,
                j.status,
                j.created_at,
                j.finished_at,
                d.endpoint,
                d.duration_ms,
                d.http_status,
                d.normalized_error_code,
                d.retryable,
                d.raw_error_excerpt,
                d.correlation_id
            from processing_subjects s
            join processing_jobs j on j.id = s.current_job_id
            left join processing_attempt_diagnostics d on d.job_id = j.id
            left join processing_content_hashes h on h.subject_id = s.id
            where s.capability = @capability
              and (s.content_hash = @content_hash or h.hash_value = @content_hash);
            """, connection);
        command.Parameters.AddWithValue("capability", capability);
        command.Parameters.AddWithValue("content_hash", contentHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadSnapshotAsync(reader, cancellationToken);
    }

    public async Task<ProcessingJobSnapshot?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                j.subject_id,
                j.id,
                j.capability,
                j.content_hash,
                j.temporary_file_key,
                j.attempt_number,
                j.status,
                j.created_at,
                j.finished_at,
                d.endpoint,
                d.duration_ms,
                d.http_status,
                d.normalized_error_code,
                d.retryable,
                d.raw_error_excerpt,
                d.correlation_id
            from processing_jobs j
            left join processing_attempt_diagnostics d on d.job_id = j.id
            where j.id = @job_id;
            """, connection);
        command.Parameters.AddWithValue("job_id", jobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadSnapshotAsync(reader, cancellationToken);
    }

    public async Task CompleteAsync(CompleteProcessingJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var updateJob = new NpgsqlCommand("""
            update processing_jobs
            set status = 'completed',
                finished_at = @finished_at,
                updated_at = @finished_at
            where id = @job_id
              and subject_id = @subject_id
              and status = 'processing';
            """, connection, transaction);
        updateJob.Parameters.AddWithValue("finished_at", command.FinishedAt);
        updateJob.Parameters.AddWithValue("job_id", command.JobId);
        updateJob.Parameters.AddWithValue("subject_id", command.SubjectId);
        await ExecuteRequiredAsync(updateJob, $"Processing job '{command.JobId}' is not in processing state.", cancellationToken);

        await UpsertDiagnosticsAsync(connection, transaction, command.JobId, command.Diagnostics, command.FinishedAt, cancellationToken);

        await using var updateSubject = new NpgsqlCommand("""
            update processing_subjects
            set state = 'completed',
                result_id = @result_id,
                updated_at = @finished_at
            where id = @subject_id
              and current_job_id = @job_id;
            """, connection, transaction);
        updateSubject.Parameters.AddWithValue("result_id", command.ResultId);
        updateSubject.Parameters.AddWithValue("finished_at", command.FinishedAt);
        updateSubject.Parameters.AddWithValue("subject_id", command.SubjectId);
        updateSubject.Parameters.AddWithValue("job_id", command.JobId);
        await ExecuteRequiredAsync(updateSubject, $"Processing subject '{command.SubjectId}' is not owned by job '{command.JobId}'.", cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeferAsync(DeferProcessingJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var updateJob = new NpgsqlCommand("""
            update processing_jobs
            set status = 'queued',
                scheduled_at = @scheduled_at,
                heartbeat_at = null,
                updated_at = @deferred_at
            where id = @job_id
              and subject_id = @subject_id
              and status = 'processing';
            """, connection, transaction);
        updateJob.Parameters.AddWithValue("scheduled_at", command.ScheduledAt);
        updateJob.Parameters.AddWithValue("deferred_at", command.DeferredAt);
        updateJob.Parameters.AddWithValue("job_id", command.JobId);
        updateJob.Parameters.AddWithValue("subject_id", command.SubjectId);
        await ExecuteRequiredAsync(updateJob, $"Processing job '{command.JobId}' is not in processing state.", cancellationToken);

        await using var updateSubject = new NpgsqlCommand("""
            update processing_subjects
            set state = 'queued',
                updated_at = @deferred_at
            where id = @subject_id
              and current_job_id = @job_id;
            """, connection, transaction);
        updateSubject.Parameters.AddWithValue("deferred_at", command.DeferredAt);
        updateSubject.Parameters.AddWithValue("subject_id", command.SubjectId);
        updateSubject.Parameters.AddWithValue("job_id", command.JobId);
        await ExecuteRequiredAsync(updateSubject, $"Processing subject '{command.SubjectId}' is not owned by job '{command.JobId}'.", cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FailAsync(FailProcessingJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var failedJob = await GetJobForRetryAsync(connection, transaction, command.JobId, cancellationToken);
        var status = command.Final ? "blocked" : "failed";

        await using var updateJob = new NpgsqlCommand("""
            update processing_jobs
            set status = @status,
                finished_at = @finished_at,
                updated_at = @finished_at
            where id = @job_id
              and subject_id = @subject_id
              and status = 'processing';
            """, connection, transaction);
        updateJob.Parameters.AddWithValue("status", status);
        updateJob.Parameters.AddWithValue("finished_at", command.FinishedAt);
        updateJob.Parameters.AddWithValue("job_id", command.JobId);
        updateJob.Parameters.AddWithValue("subject_id", command.SubjectId);
        await ExecuteRequiredAsync(updateJob, $"Processing job '{command.JobId}' is not in processing state.", cancellationToken);

        var diagnostics = command.Diagnostics with
        {
            NormalizedError = command.Error,
            Retryable = ProcessorErrorClassifier.Classify(command.Error).IsRetryable
        };
        await UpsertDiagnosticsAsync(connection, transaction, command.JobId, diagnostics, command.FinishedAt, cancellationToken);

        if (!command.Final)
        {
            var retryJobId = Guid.NewGuid();
            var retryAttemptNumber = failedJob.AttemptNumber + 1;
            var scheduledAt = command.FinishedAt.Add(RetryDelay);

            await using var insertRetry = new NpgsqlCommand("""
                insert into processing_jobs (
                    id,
                    subject_id,
                    capability,
                    content_hash,
                    temporary_file_key,
                    attempt_number,
                    status,
                    scheduled_at,
                    created_at,
                    updated_at)
                values (
                    @id,
                    @subject_id,
                    @capability,
                    @content_hash,
                    @temporary_file_key,
                    @attempt_number,
                    'queued',
                    @scheduled_at,
                    @created_at,
                    @updated_at);
                """, connection, transaction);
            insertRetry.Parameters.AddWithValue("id", retryJobId);
            insertRetry.Parameters.AddWithValue("subject_id", command.SubjectId);
            insertRetry.Parameters.AddWithValue("capability", failedJob.Capability);
            insertRetry.Parameters.AddWithValue("content_hash", failedJob.ContentHash);
            insertRetry.Parameters.AddWithValue("temporary_file_key", failedJob.TemporaryFileKey);
            insertRetry.Parameters.AddWithValue("attempt_number", retryAttemptNumber);
            insertRetry.Parameters.AddWithValue("scheduled_at", scheduledAt);
            insertRetry.Parameters.AddWithValue("created_at", command.FinishedAt);
            insertRetry.Parameters.AddWithValue("updated_at", command.FinishedAt);
            await insertRetry.ExecuteNonQueryAsync(cancellationToken);

            await using var updateSubjectForRetry = new NpgsqlCommand("""
                update processing_subjects
                set current_job_id = @current_job_id,
                    state = 'queued',
                    updated_at = @finished_at
                where id = @subject_id
                  and current_job_id = @failed_job_id;
                """, connection, transaction);
            updateSubjectForRetry.Parameters.AddWithValue("current_job_id", retryJobId);
            updateSubjectForRetry.Parameters.AddWithValue("finished_at", command.FinishedAt);
            updateSubjectForRetry.Parameters.AddWithValue("subject_id", command.SubjectId);
            updateSubjectForRetry.Parameters.AddWithValue("failed_job_id", command.JobId);
            await ExecuteRequiredAsync(updateSubjectForRetry, $"Processing subject '{command.SubjectId}' is not owned by job '{command.JobId}'.", cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await using var updateSubject = new NpgsqlCommand("""
            update processing_subjects
            set state = @status,
                updated_at = @finished_at
            where id = @subject_id
              and current_job_id = @job_id;
            """, connection, transaction);
        updateSubject.Parameters.AddWithValue("status", status);
        updateSubject.Parameters.AddWithValue("finished_at", command.FinishedAt);
        updateSubject.Parameters.AddWithValue("subject_id", command.SubjectId);
        updateSubject.Parameters.AddWithValue("job_id", command.JobId);
        await ExecuteRequiredAsync(updateSubject, $"Processing subject '{command.SubjectId}' is not owned by job '{command.JobId}'.", cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<JobForRetry> GetJobForRetryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                capability,
                content_hash,
                temporary_file_key,
                attempt_number
            from processing_jobs
            where id = @job_id
            for update;
            """, connection, transaction);
        command.Parameters.AddWithValue("job_id", jobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Processing job '{jobId}' was not found.");
        }

        return new JobForRetry(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3));
    }

    private static async Task<ExistingSubject?> FindSubjectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CreateProcessingJobCommand command,
        CancellationToken cancellationToken)
    {
        await using var find = new NpgsqlCommand("""
            select
                s.id,
                s.current_job_id,
                j.attempt_number,
                j.status
            from processing_subjects s
            left join processing_jobs j on j.id = s.current_job_id
            left join processing_content_hashes h on h.subject_id = s.id
            where s.capability = @capability
              and (
                  s.content_hash = any(@content_hashes)
                  or h.hash_value = any(@content_hashes)
              )
            for update of s;
            """, connection, transaction);
        find.Parameters.AddWithValue("capability", command.Capability);
        find.Parameters.AddWithValue("content_hashes", ResolveHashValues(command).ToArray());

        await using var reader = await find.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ExistingSubject(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task<int> GetNextAttemptNumberAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select coalesce(max(attempt_number), 0) + 1
            from processing_jobs
            where subject_id = @subject_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("subject_id", subjectId);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static bool IsActive(string? status)
    {
        return status is "queued" or "processing";
    }

    private static async Task<ProcessingJobSnapshot?> ReadSnapshotAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ProcessingJobSnapshot(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            ProcessingJobStatusMapper.Parse(reader.GetString(6)),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            ReadDiagnostics(reader));
    }

    private static AttemptDiagnostics? ReadDiagnostics(NpgsqlDataReader reader)
    {
        if (reader.IsDBNull(15))
        {
            return null;
        }

        return new AttemptDiagnostics(
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : TimeSpan.FromMilliseconds(reader.GetInt32(10)),
            reader.IsDBNull(11) ? null : reader.GetInt32(11),
            reader.IsDBNull(12) ? null : Enum.Parse<NormalizedProcessorError>(reader.GetString(12)),
            reader.IsDBNull(13) ? null : reader.GetBoolean(13),
            reader.GetString(15),
            reader.IsDBNull(14) ? null : reader.GetString(14));
    }

    private sealed record ExistingSubject(
        Guid SubjectId,
        Guid? CurrentJobId,
        int? CurrentAttemptNumber,
        string? CurrentJobStatus);

    private sealed record JobForRetry(
        string Capability,
        string ContentHash,
        string TemporaryFileKey,
        int AttemptNumber);

    private static async Task UpsertDiagnosticsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid jobId,
        AttemptDiagnostics diagnostics,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into processing_attempt_diagnostics (
                job_id,
                endpoint,
                duration_ms,
                http_status,
                normalized_error_code,
                retryable,
                raw_error_excerpt,
                correlation_id,
                created_at)
            values (
                @job_id,
                @endpoint,
                @duration_ms,
                @http_status,
                @normalized_error_code,
                @retryable,
                @raw_error_excerpt,
                @correlation_id,
                @created_at)
            on conflict (job_id) do update
            set endpoint = excluded.endpoint,
                duration_ms = excluded.duration_ms,
                http_status = excluded.http_status,
                normalized_error_code = excluded.normalized_error_code,
                retryable = excluded.retryable,
                raw_error_excerpt = excluded.raw_error_excerpt,
                correlation_id = excluded.correlation_id,
                created_at = excluded.created_at;
            """, connection, transaction);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("endpoint", (object?)diagnostics.Endpoint ?? DBNull.Value);
        command.Parameters.AddWithValue("duration_ms", (object?)ToDurationMilliseconds(diagnostics.Duration) ?? DBNull.Value);
        command.Parameters.AddWithValue("http_status", (object?)diagnostics.HttpStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("normalized_error_code", (object?)diagnostics.NormalizedError?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("retryable", (object?)diagnostics.Retryable ?? DBNull.Value);
        command.Parameters.AddWithValue("raw_error_excerpt", (object?)diagnostics.RawErrorExcerpt ?? DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", diagnostics.CorrelationId);
        command.Parameters.AddWithValue("created_at", createdAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static int? ToDurationMilliseconds(TimeSpan? duration)
    {
        return duration is null ? null : Convert.ToInt32(duration.Value.TotalMilliseconds);
    }

    private static async Task UpsertContentHashesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid subjectId,
        CreateProcessingJobCommand command,
        CancellationToken cancellationToken)
    {
        var hashes = ResolveContentHashes(command).ToArray();
        foreach (var hash in hashes)
        {
            await using var insert = new NpgsqlCommand("""
                insert into processing_content_hashes (
                    subject_id,
                    capability,
                    algorithm,
                    hash_value,
                    created_at)
                values (
                    @subject_id,
                    @capability,
                    @algorithm,
                    @hash_value,
                    @created_at)
                on conflict (capability, hash_value) do nothing;
                """, connection, transaction);
            insert.Parameters.AddWithValue("subject_id", subjectId);
            insert.Parameters.AddWithValue("capability", command.Capability);
            insert.Parameters.AddWithValue("algorithm", hash.Algorithm);
            insert.Parameters.AddWithValue("hash_value", hash.HashValue);
            insert.Parameters.AddWithValue("created_at", command.CreatedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IReadOnlyList<ProcessingContentHash> ResolveContentHashes(CreateProcessingJobCommand command)
    {
        if (command.ContentHashes is { Count: > 0 })
        {
            return command.ContentHashes;
        }

        var algorithm = command.ContentHash.Split(':', 2)[0];
        return [new ProcessingContentHash(algorithm, command.ContentHash)];
    }

    private static IReadOnlyList<string> ResolveHashValues(CreateProcessingJobCommand command)
    {
        return ResolveContentHashes(command)
            .Select(hash => hash.HashValue)
            .Append(command.ContentHash)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task ExecuteRequiredAsync(
        NpgsqlCommand command,
        string message,
        CancellationToken cancellationToken)
    {
        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(message);
        }
    }
}
