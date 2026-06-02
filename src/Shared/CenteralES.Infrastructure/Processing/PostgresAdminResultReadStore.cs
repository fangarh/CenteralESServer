using System.Text.Json;
using CenteralES.Admin;
using Npgsql;

namespace CenteralES.Infrastructure.Processing;

public sealed class PostgresAdminResultReadStore : IAdminResultReadStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAdminResultReadStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
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
              and (
                  @content_hash::text is null
                  or i.content_hash = @content_hash
                  or exists (
                      select 1
                      from processing_content_hashes h
                      where h.subject_id = i.subject_id
                        and h.hash_value = @content_hash
                  )
              )
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
            results.Add(PostgresAdminReadStoreHelpers.ReadResultReferenceRow(reader));
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

        var reference = PostgresAdminReadStoreHelpers.ReadResultReferenceRow(reader);
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
            .Select(PostgresAdminReadStoreHelpers.ToSafeExcerpt)
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
