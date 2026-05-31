using CenteralES.Processing;
using CenteralES.Processing.Queue;
using Npgsql;

namespace CenteralES.Infrastructure.Processing;

public sealed class PostgresProcessingJobQueue : IProcessingJobQueue
{
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
            await transaction.CommitAsync(cancellationToken);
            return new EnqueueProcessingJobResult(
                existing.SubjectId,
                existing.CurrentJobId!.Value,
                existing.CurrentAttemptNumber!.Value,
                ParseStatus(existing.CurrentJobStatus!),
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
            where s.capability = @capability
              and s.content_hash = @content_hash
            for update of s;
            """, connection, transaction);
        find.Parameters.AddWithValue("capability", command.Capability);
        find.Parameters.AddWithValue("content_hash", command.ContentHash);

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

    private static ProcessingJobStatus ParseStatus(string status)
    {
        return status switch
        {
            "queued" => ProcessingJobStatus.Queued,
            "processing" => ProcessingJobStatus.Processing,
            "completed" => ProcessingJobStatus.Completed,
            "failed" => ProcessingJobStatus.Failed,
            "blocked" => ProcessingJobStatus.Blocked,
            "cancelled" => ProcessingJobStatus.Cancelled,
            _ => throw new InvalidOperationException($"Unknown job status '{status}'.")
        };
    }

    private sealed record ExistingSubject(
        Guid SubjectId,
        Guid? CurrentJobId,
        int? CurrentAttemptNumber,
        string? CurrentJobStatus);
}
