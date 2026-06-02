using CenteralES.Admin;
using CenteralES.Processing;
using Npgsql;

namespace CenteralES.Infrastructure.Processing;

public sealed class PostgresAdminJobReadStore : IAdminJobReadStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IAdminProcessorReadStore _processorStore;

    public PostgresAdminJobReadStore(
        NpgsqlDataSource dataSource,
        IAdminProcessorReadStore processorStore)
    {
        _dataSource = dataSource;
        _processorStore = processorStore;
    }

    public async Task<IReadOnlyList<AdminProcessingJobListItem>> ListJobsAsync(
        AdminProcessingJobListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit, 1, 200);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                j.id,
                j.subject_id,
                j.capability,
                j.content_hash,
                j.attempt_number,
                j.status,
                j.created_at,
                j.started_at,
                j.finished_at,
                d.endpoint,
                d.normalized_error_code,
                d.retryable,
                d.correlation_id
            from processing_jobs j
            left join processing_attempt_diagnostics d on d.job_id = j.id
            where (@capability::text is null or j.capability = @capability)
              and (@status::text is null or j.status = @status)
              and (
                  @content_hash::text is null
                  or j.content_hash = @content_hash
                  or exists (
                      select 1
                      from processing_content_hashes h
                      where h.subject_id = j.subject_id
                        and h.hash_value = @content_hash
                  )
              )
            order by j.created_at desc
            limit @limit;
            """, connection);
        command.Parameters.AddWithValue("capability", (object?)query.Capability ?? DBNull.Value);
        command.Parameters.AddWithValue("status", (object?)query.Status?.ToDatabaseValue() ?? DBNull.Value);
        command.Parameters.AddWithValue("content_hash", (object?)query.ContentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit);

        var jobs = new List<AdminProcessingJobListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new AdminProcessingJobListItem(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                ProcessingJobStatusMapper.Parse(reader.GetString(5)),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : Enum.Parse<NormalizedProcessorError>(reader.GetString(10)),
                reader.IsDBNull(11) ? null : reader.GetBoolean(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return jobs;
    }

    public async Task<AdminProcessingJobDetails?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                j.id,
                j.subject_id,
                j.capability,
                j.content_hash,
                j.temporary_file_key,
                j.attempt_number,
                j.status,
                j.scheduled_at,
                j.started_at,
                j.finished_at,
                j.heartbeat_at,
                j.created_at,
                j.updated_at,
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
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var details = new AdminProcessingJobDetails(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            ProcessingJobStatusMapper.Parse(reader.GetString(6)),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetFieldValue<DateTimeOffset>(11),
            reader.GetFieldValue<DateTimeOffset>(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : TimeSpan.FromMilliseconds(reader.GetInt32(14)),
            reader.IsDBNull(15) ? null : reader.GetInt32(15),
            reader.IsDBNull(16) ? null : Enum.Parse<NormalizedProcessorError>(reader.GetString(16)),
            reader.IsDBNull(17) ? null : reader.GetBoolean(17),
            reader.IsDBNull(18) ? null : reader.GetString(18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            Array.Empty<AdminProcessingAttemptDetails>());

        await reader.DisposeAsync();

        var attempts = await PostgresAdminReadStoreHelpers.ReadAttemptsAsync(connection, details.SubjectId, cancellationToken);
        return details with { Attempts = attempts };
    }

    public async Task<AdminJobSupportReport?> GetJobSupportReportAsync(
        Guid jobId,
        string processorKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processorKey);

        var job = await GetJobAsync(jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await PostgresAdminReadStoreHelpers.ReadResultReferenceAsync(connection, job.SubjectId, cancellationToken);
        var auditEvents = await PostgresAdminReadStoreHelpers.ReadAuditEventsAsync(
            connection,
            job.Attempts.Select(attempt => attempt.JobId).Append(job.JobId),
            cancellationToken);
        var processor = await _processorStore.GetProcessorStatusAsync(
            processorKey,
            job.Capability,
            recentDiagnosticsLimit: 10,
            cancellationToken);

        return new AdminJobSupportReport(
            DateTimeOffset.UtcNow,
            job.JobId,
            job.SubjectId,
            job.Capability,
            processorKey,
            job.ContentHash,
            job.AttemptNumber,
            job.Status,
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt,
            job.HeartbeatAt,
            new AdminJobSupportReportDiagnostics(
                job.Endpoint,
                job.Duration,
                job.HttpStatus,
                job.NormalizedError,
                job.Retryable,
                job.CorrelationId,
                PostgresAdminReadStoreHelpers.ToSafeExcerpt(job.RawErrorExcerpt)),
            job.Attempts,
            result,
            processor,
            auditEvents);
    }
}
