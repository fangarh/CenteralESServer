using CenteralES.Admin;
using CenteralES.Processing;
using Npgsql;

namespace CenteralES.Infrastructure.Processing;

public sealed class PostgresAdminProcessingReadStore : IAdminProcessingReadStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAdminProcessingReadStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
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
              and (@content_hash::text is null or j.content_hash = @content_hash)
            order by j.created_at desc
            limit @limit;
            """, connection);
        command.Parameters.AddWithValue("capability", (object?)query.Capability ?? DBNull.Value);
        command.Parameters.AddWithValue("status", (object?)ToDatabaseStatus(query.Status) ?? DBNull.Value);
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
                ParseStatus(reader.GetString(5)),
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
            ParseStatus(reader.GetString(6)),
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

        var attempts = await ReadAttemptsAsync(connection, details.SubjectId, cancellationToken);
        return details with { Attempts = attempts };
    }

    public async Task<AdminProcessorStatus> GetProcessorStatusAsync(
        string processorKey,
        string capability,
        int recentDiagnosticsLimit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processorKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        var limit = Math.Clamp(recentDiagnosticsLimit, 1, 50);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var queue = await ReadQueueCountsAsync(connection, capability, cancellationToken);
        var workers = await ReadWorkersAsync(connection, processorKey, capability, DateTimeOffset.UtcNow, cancellationToken);
        var diagnostics = await ReadRecentDiagnosticsAsync(connection, capability, limit, cancellationToken);

        return new AdminProcessorStatus(
            processorKey,
            capability,
            Health: ToProcessorHealth(workers),
            queue,
            workers,
            diagnostics);
    }

    private static string ToProcessorHealth(IReadOnlyList<AdminProcessorWorkerStatus> workers)
    {
        if (workers.Count == 0)
        {
            return "unknown";
        }

        return workers.Any(worker => !worker.Stale) ? "healthy" : "unhealthy";
    }

    private static async Task<IReadOnlyList<AdminProcessorWorkerStatus>> ReadWorkersAsync(
        NpgsqlConnection connection,
        string processorKey,
        string capability,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                worker_id,
                started_at,
                heartbeat_at
            from processing_worker_heartbeats
            where processor_key = @processor_key
              and capability = @capability
            order by heartbeat_at desc;
            """, connection);
        command.Parameters.AddWithValue("processor_key", processorKey);
        command.Parameters.AddWithValue("capability", capability);

        var workers = new List<AdminProcessorWorkerStatus>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var heartbeatAt = reader.GetFieldValue<DateTimeOffset>(2);
            workers.Add(new AdminProcessorWorkerStatus(
                reader.GetString(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                heartbeatAt,
                Stale: now - heartbeatAt > TimeSpan.FromMinutes(3)));
        }

        return workers;
    }

    private static async Task<AdminProcessorQueueCounts> ReadQueueCountsAsync(
        NpgsqlConnection connection,
        string capability,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                count(*) filter (where status = 'queued')::int,
                count(*) filter (where status = 'processing')::int,
                count(*) filter (where status = 'completed')::int,
                count(*) filter (where status = 'failed')::int,
                count(*) filter (where status = 'blocked')::int,
                count(*) filter (where status = 'cancelled')::int
            from processing_jobs
            where capability = @capability;
            """, connection);
        command.Parameters.AddWithValue("capability", capability);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Processor queue count query returned no rows.");
        }

        return new AdminProcessorQueueCounts(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5));
    }

    private static async Task<IReadOnlyList<AdminProcessorRecentDiagnostic>> ReadRecentDiagnosticsAsync(
        NpgsqlConnection connection,
        string capability,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                j.id,
                j.attempt_number,
                j.status,
                d.endpoint,
                d.http_status,
                d.normalized_error_code,
                d.retryable,
                d.correlation_id,
                d.created_at
            from processing_attempt_diagnostics d
            join processing_jobs j on j.id = d.job_id
            where j.capability = @capability
            order by d.created_at desc
            limit @limit;
            """, connection);
        command.Parameters.AddWithValue("capability", capability);
        command.Parameters.AddWithValue("limit", limit);

        var diagnostics = new List<AdminProcessorRecentDiagnostic>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            diagnostics.Add(new AdminProcessorRecentDiagnostic(
                reader.GetGuid(0),
                reader.GetInt32(1),
                ParseStatus(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : Enum.Parse<NormalizedProcessorError>(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetBoolean(6),
                reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return diagnostics;
    }

    private static async Task<IReadOnlyList<AdminProcessingAttemptDetails>> ReadAttemptsAsync(
        NpgsqlConnection connection,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                j.id,
                j.attempt_number,
                j.status,
                j.created_at,
                j.started_at,
                j.finished_at,
                d.endpoint,
                d.duration_ms,
                d.http_status,
                d.normalized_error_code,
                d.retryable,
                d.correlation_id
            from processing_jobs j
            left join processing_attempt_diagnostics d on d.job_id = j.id
            where j.subject_id = @subject_id
            order by j.attempt_number;
            """, connection);
        command.Parameters.AddWithValue("subject_id", subjectId);

        var attempts = new List<AdminProcessingAttemptDetails>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attempts.Add(new AdminProcessingAttemptDetails(
                reader.GetGuid(0),
                reader.GetInt32(1),
                ParseStatus(reader.GetString(2)),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : TimeSpan.FromMilliseconds(reader.GetInt32(7)),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : Enum.Parse<NormalizedProcessorError>(reader.GetString(9)),
                reader.IsDBNull(10) ? null : reader.GetBoolean(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return attempts;
    }

    private static string? ToDatabaseStatus(ProcessingJobStatus? status)
    {
        return status is null
            ? null
            : status.Value switch
            {
                ProcessingJobStatus.Queued => "queued",
                ProcessingJobStatus.Processing => "processing",
                ProcessingJobStatus.Completed => "completed",
                ProcessingJobStatus.Failed => "failed",
                ProcessingJobStatus.Blocked => "blocked",
                ProcessingJobStatus.Cancelled => "cancelled",
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown processing job status.")
            };
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
}
