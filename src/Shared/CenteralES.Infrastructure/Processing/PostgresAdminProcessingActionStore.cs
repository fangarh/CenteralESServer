using System.Text.Json;
using CenteralES.Admin;
using Npgsql;
using NpgsqlTypes;

namespace CenteralES.Infrastructure.Processing;

public sealed class PostgresAdminProcessingActionStore : IAdminProcessingActionStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAdminProcessingActionStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<AdminManualRetryJobResult> ManualRetryJobAsync(
        AdminManualRetryJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var job = await ReadJobForManualRetryAsync(connection, transaction, command.SourceJobId, cancellationToken);
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return AdminManualRetryJobResult.NotFound();
        }

        if (!CanManualRetry(job))
        {
            await transaction.CommitAsync(cancellationToken);
            return AdminManualRetryJobResult.Conflict(command.SourceJobId);
        }

        var newJobId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        var attemptNumber = await GetNextAttemptNumberAsync(connection, transaction, job.SubjectId, cancellationToken);

        await InsertRetryJobAsync(
            connection,
            transaction,
            newJobId,
            job,
            attemptNumber,
            command.RequestedAt,
            cancellationToken);

        await UpdateSubjectAsync(
            connection,
            transaction,
            job.SubjectId,
            newJobId,
            command.RequestedAt,
            cancellationToken);

        await InsertAuditEventAsync(
            connection,
            transaction,
            auditId,
            command,
            job,
            newJobId,
            attemptNumber,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return AdminManualRetryJobResult.Success(
            command.SourceJobId,
            newJobId,
            job.ContentHash,
            attemptNumber,
            auditId);
    }

    private static bool CanManualRetry(JobForManualRetry job)
    {
        return job.CurrentJobId == job.JobId
            && job.Status is "failed" or "blocked";
    }

    private static async Task<JobForManualRetry?> ReadJobForManualRetryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                job.id,
                job.subject_id,
                subject.current_job_id,
                job.capability,
                job.content_hash,
                job.temporary_file_key,
                job.attempt_number,
                job.status
            from processing_jobs job
            inner join processing_subjects subject on subject.id = job.subject_id
            where job.id = @job_id
            for update of subject, job;
            """, connection, transaction);
        command.Parameters.AddWithValue("job_id", jobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new JobForManualRetry(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetString(7));
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

    private static async Task InsertRetryJobAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid newJobId,
        JobForManualRetry source,
        int attemptNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
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
        command.Parameters.AddWithValue("id", newJobId);
        command.Parameters.AddWithValue("subject_id", source.SubjectId);
        command.Parameters.AddWithValue("capability", source.Capability);
        command.Parameters.AddWithValue("content_hash", source.ContentHash);
        command.Parameters.AddWithValue("temporary_file_key", source.TemporaryFileKey);
        command.Parameters.AddWithValue("attempt_number", attemptNumber);
        command.Parameters.AddWithValue("scheduled_at", now);
        command.Parameters.AddWithValue("created_at", now);
        command.Parameters.AddWithValue("updated_at", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateSubjectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid subjectId,
        Guid newJobId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            update processing_subjects
            set current_job_id = @current_job_id,
                state = 'queued',
                result_id = null,
                updated_at = @updated_at
            where id = @subject_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("current_job_id", newJobId);
        command.Parameters.AddWithValue("updated_at", now);
        command.Parameters.AddWithValue("subject_id", subjectId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid auditId,
        AdminManualRetryJobCommand command,
        JobForManualRetry source,
        Guid newJobId,
        int attemptNumber,
        CancellationToken cancellationToken)
    {
        await using var insertAudit = new NpgsqlCommand("""
            insert into admin_audit_events (
                id,
                occurred_at,
                actor_admin_id,
                actor_login,
                action,
                target_type,
                target_id,
                old_value_json,
                new_value_json,
                comment,
                correlation_id,
                ip,
                user_agent,
                technical_metadata_json)
            values (
                @id,
                @occurred_at,
                @actor_admin_id,
                @actor_login,
                @action,
                @target_type,
                @target_id,
                @old_value_json,
                @new_value_json,
                @comment,
                @correlation_id,
                @ip,
                @user_agent,
                @technical_metadata_json);
            """, connection, transaction);

        insertAudit.Parameters.AddWithValue("id", auditId);
        insertAudit.Parameters.AddWithValue("occurred_at", command.RequestedAt);
        insertAudit.Parameters.AddWithValue("actor_admin_id", command.ActorAdminId);
        insertAudit.Parameters.AddWithValue("actor_login", command.ActorLogin);
        insertAudit.Parameters.AddWithValue("action", AdminAuditActions.ManualRetryJob);
        insertAudit.Parameters.AddWithValue("target_type", AdminAuditTargetTypes.ProcessingJob);
        insertAudit.Parameters.AddWithValue("target_id", command.SourceJobId.ToString("N"));
        AddJsonParameter(insertAudit, "old_value_json", new
        {
            jobId = source.JobId.ToString("N"),
            attemptNumber = source.AttemptNumber,
            status = source.Status,
            hash = source.ContentHash
        });
        AddJsonParameter(insertAudit, "new_value_json", new
        {
            jobId = newJobId.ToString("N"),
            attemptNumber,
            status = "queued",
            hash = source.ContentHash
        });
        insertAudit.Parameters.AddWithValue("comment", (object?)NormalizeComment(command.Comment) ?? DBNull.Value);
        insertAudit.Parameters.AddWithValue("correlation_id", Guid.NewGuid().ToString("N"));
        insertAudit.Parameters.AddWithValue("ip", (object?)command.IpAddress ?? DBNull.Value);
        insertAudit.Parameters.AddWithValue("user_agent", (object?)command.UserAgent ?? DBNull.Value);
        AddJsonParameter(insertAudit, "technical_metadata_json", new
        {
            capability = source.Capability,
            subjectId = source.SubjectId.ToString("N")
        });

        await insertAudit.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? NormalizeComment(string? comment)
    {
        return string.IsNullOrWhiteSpace(comment)
            ? null
            : comment.Trim();
    }

    private static void AddJsonParameter(NpgsqlCommand command, string name, object value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(value)
        });
    }

    private sealed record JobForManualRetry(
        Guid JobId,
        Guid SubjectId,
        Guid? CurrentJobId,
        string Capability,
        string ContentHash,
        string TemporaryFileKey,
        int AttemptNumber,
        string Status);
}
