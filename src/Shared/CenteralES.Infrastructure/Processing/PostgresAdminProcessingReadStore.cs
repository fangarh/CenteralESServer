using System.Text.Json;
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

        var attempts = await ReadAttemptsAsync(connection, details.SubjectId, cancellationToken);
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
        var result = await ReadResultReferenceAsync(connection, job.SubjectId, cancellationToken);
        var auditEvents = await ReadAuditEventsAsync(
            connection,
            job.Attempts.Select(attempt => attempt.JobId).Append(job.JobId),
            cancellationToken);
        var processor = await GetProcessorStatusAsync(
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
                ToSafeExcerpt(job.RawErrorExcerpt)),
            job.Attempts,
            result,
            processor,
            auditEvents);
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

    public async Task<IReadOnlyList<AdminAuditEventListItem>> ListAuditEventsAsync(
        AdminAuditEventListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit, 1, 200);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                id,
                occurred_at,
                actor_admin_id,
                actor_login,
                action,
                target_type,
                target_id,
                comment,
                correlation_id
            from admin_audit_events
            where (@action::text is null or action = @action)
              and (@target_type::text is null or target_type = @target_type)
              and (@target_id::text is null or target_id = @target_id)
              and (@actor_login::text is null or actor_login = @actor_login)
              and (@occurred_from::timestamptz is null or occurred_at >= @occurred_from)
              and (@occurred_to::timestamptz is null or occurred_at <= @occurred_to)
            order by occurred_at desc
            limit @limit;
            """, connection);
        command.Parameters.AddWithValue("action", (object?)query.Action ?? DBNull.Value);
        command.Parameters.AddWithValue("target_type", (object?)query.TargetType ?? DBNull.Value);
        command.Parameters.AddWithValue("target_id", (object?)query.TargetId ?? DBNull.Value);
        command.Parameters.AddWithValue("actor_login", (object?)query.ActorLogin ?? DBNull.Value);
        command.Parameters.AddWithValue("occurred_from", (object?)query.OccurredFrom ?? DBNull.Value);
        command.Parameters.AddWithValue("occurred_to", (object?)query.OccurredTo ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit);

        var events = new List<AdminAuditEventListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new AdminAuditEventListItem(
                reader.GetGuid(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : ToSafeExcerpt(reader.GetString(7)),
                reader.GetString(8)));
        }

        return events;
    }

    public async Task<IReadOnlyList<AdminResultReference>> ListResultsAsync(
        AdminResultListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit, 1, 200);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                i.id,
                i.subject_id,
                i.job_id,
                i.capability,
                i.content_hash,
                i.result_kind,
                i.payload_table,
                i.payload_id,
                i.contract_version,
                i.payload_size,
                i.created_at,
                j.status,
                j.attempt_number
            from processing_result_index i
            left join processing_jobs j on j.id = i.job_id
            where (@capability::text is null or i.capability = @capability)
              and (@content_hash::text is null or i.content_hash = @content_hash)
              and (@job_id::uuid is null or i.job_id = @job_id)
            order by i.created_at desc
            limit @limit;
            """, connection);
        command.Parameters.AddWithValue("capability", (object?)query.Capability ?? DBNull.Value);
        command.Parameters.AddWithValue("content_hash", (object?)query.ContentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("job_id", (object?)query.JobId ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit);

        var results = new List<AdminResultReference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadResultReferenceRow(reader));
        }

        return results;
    }

    public async Task<AdminResultDetails?> GetResultAsync(Guid resultIndexId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                i.id,
                i.subject_id,
                i.job_id,
                i.capability,
                i.content_hash,
                i.result_kind,
                i.payload_table,
                i.payload_id,
                i.contract_version,
                i.payload_size,
                i.created_at,
                j.status,
                j.attempt_number
            from processing_result_index i
            left join processing_jobs j on j.id = i.job_id
            where i.id = @result_index_id;
            """, connection);
        command.Parameters.AddWithValue("result_index_id", resultIndexId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var reference = ReadResultReferenceRow(reader);
        await reader.DisposeAsync();

        var summary = reference.PayloadTable == "pdf_stamp_recognition_results"
            ? await ReadPdfStampRecognitionSummaryAsync(connection, reference.PayloadId, cancellationToken)
            : null;

        return new AdminResultDetails(reference, summary);
    }

    public async Task<AdminPdfStampRecognitionPayload?> GetPdfStampRecognitionPayloadAsync(
        Guid payloadId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, payload_json::text
            from pdf_stamp_recognition_results
            where id = @payload_id;
            """, connection);
        command.Parameters.AddWithValue("payload_id", payloadId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdminPdfStampRecognitionPayload(
            reader.GetGuid(0),
            reader.GetString(1));
    }

    private static async Task<AdminPdfStampRecognitionResultSummary?> ReadPdfStampRecognitionSummaryAsync(
        NpgsqlConnection connection,
        Guid payloadId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select payload_json::text
            from pdf_stamp_recognition_results
            where id = @payload_id;
            """, connection);
        command.Parameters.AddWithValue("payload_id", payloadId);

        var payloadJson = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;

        var pageKeys = ReadObjectKeys(root, "workers_page");
        var errorExcerpts = ReadStringArray(root, "errors")
            .Select(ToSafeExcerpt)
            .Where(value => value is not null)
            .Cast<string>()
            .Take(5)
            .ToArray();

        return new AdminPdfStampRecognitionResultSummary(
            WorkerGroupCount: CountArrayItems(root, "workers"),
            WorkerTextItemCount: CountNestedWorkerTextItems(root),
            WorkerPageCount: pageKeys.Count,
            UnrecognizedPageCount: CountArrayItems(root, "unrecognized_pages"),
            ErrorCount: CountArrayItems(root, "errors"),
            IzmNumber: ReadString(root, "izm_number"),
            PageKeys: pageKeys,
            ErrorExcerpts: errorExcerpts);
    }

    private static string ToProcessorHealth(IReadOnlyList<AdminProcessorWorkerStatus> workers)
    {
        if (workers.Count == 0)
        {
            return "unknown";
        }

        return workers.Any(worker => !worker.Stale) ? "healthy" : "unhealthy";
    }

    private static AdminResultReference ReadResultReferenceRow(NpgsqlDataReader reader)
    {
        return new AdminResultReference(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetGuid(7),
            reader.GetString(8),
            reader.GetInt64(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : ProcessingJobStatusMapper.Parse(reader.GetString(11)),
            reader.IsDBNull(12) ? null : reader.GetInt32(12));
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
                ProcessingJobStatusMapper.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : Enum.Parse<NormalizedProcessorError>(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetBoolean(6),
                reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return diagnostics;
    }

    private static async Task<AdminJobSupportReportResultReference?> ReadResultReferenceAsync(
        NpgsqlConnection connection,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                id,
                result_kind,
                payload_table,
                payload_id,
                contract_version,
                payload_size,
                created_at
            from processing_result_index
            where subject_id = @subject_id;
            """, connection);
        command.Parameters.AddWithValue("subject_id", subjectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdminJobSupportReportResultReference(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetInt64(5),
            reader.GetFieldValue<DateTimeOffset>(6));
    }

    private static async Task<IReadOnlyList<AdminJobSupportReportAuditEvent>> ReadAuditEventsAsync(
        NpgsqlConnection connection,
        IEnumerable<Guid> jobIds,
        CancellationToken cancellationToken)
    {
        var targetIds = jobIds
            .Distinct()
            .Select(jobId => jobId.ToString("N"))
            .ToArray();
        if (targetIds.Length == 0)
        {
            return Array.Empty<AdminJobSupportReportAuditEvent>();
        }

        await using var command = new NpgsqlCommand("""
            select
                id,
                occurred_at,
                actor_login,
                action,
                target_type,
                target_id,
                comment,
                correlation_id
            from admin_audit_events
            where target_type = 'processing_job'
              and target_id = any(@target_ids)
            order by occurred_at desc
            limit 20;
            """, connection);
        command.Parameters.AddWithValue("target_ids", targetIds);

        var events = new List<AdminJobSupportReportAuditEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new AdminJobSupportReportAuditEvent(
                reader.GetGuid(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : ToSafeExcerpt(reader.GetString(6)),
                reader.GetString(7)));
        }

        return events;
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
                ProcessingJobStatusMapper.Parse(reader.GetString(2)),
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

    private static string? ToSafeExcerpt(string? value)
    {
        const int maxLength = 2000;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : string.Concat(normalized.AsSpan(0, maxLength), "...");
    }

    private static int CountArrayItems(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;
    }

    private static int CountNestedWorkerTextItems(JsonElement root)
    {
        if (!root.TryGetProperty("workers", out var workers) || workers.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var count = 0;
        foreach (var group in workers.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in group.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }

    private static IReadOnlyList<string> ReadObjectKeys(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

}
