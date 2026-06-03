using CenteralES.Admin;
using CenteralES.Processing;
using Npgsql;

namespace CenteralES.Infrastructure.Processing;

internal static class PostgresAdminReadStoreHelpers
{
    public static AdminResultReference ReadResultReferenceRow(NpgsqlDataReader reader)
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

    public static async Task<AdminJobSupportReportResultReference?> ReadResultReferenceAsync(
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

    public static async Task<IReadOnlyList<AdminJobSupportReportAuditEvent>> ReadAuditEventsAsync(
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

    public static async Task<IReadOnlyList<AdminProcessingAttemptDetails>> ReadAttemptsAsync(
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

    public static string? ToSafeExcerpt(string? value)
    {
        const int maxLength = 2000;
        string[] sensitiveMarkers = ["secret", "password", "token"];

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (sensitiveMarkers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return "[redacted sensitive diagnostic]";
        }

        return normalized.Length <= maxLength
            ? normalized
            : string.Concat(normalized.AsSpan(0, maxLength), "...");
    }
}
