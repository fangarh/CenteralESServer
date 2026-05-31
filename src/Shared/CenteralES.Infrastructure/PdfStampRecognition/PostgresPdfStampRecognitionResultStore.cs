using System.Text;
using CenteralES.PdfStampRecognition;
using Npgsql;
using NpgsqlTypes;

namespace CenteralES.Infrastructure.PdfStampRecognition;

public sealed class PostgresPdfStampRecognitionResultStore : IPdfStampRecognitionResultStore
{
    private const string PayloadTable = "pdf_stamp_recognition_results";
    private readonly NpgsqlDataSource _dataSource;

    public PostgresPdfStampRecognitionResultStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<PdfStampRecognitionResult> SaveAsync(SavePdfStampRecognitionResultCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resultIndexId = Guid.NewGuid();
        var payloadId = Guid.NewGuid();
        var payloadSize = Encoding.UTF8.GetByteCount(command.PayloadJson);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var insertIndex = new NpgsqlCommand("""
            insert into processing_result_index (
                id,
                subject_id,
                capability,
                content_hash,
                job_id,
                result_kind,
                payload_table,
                payload_id,
                contract_version,
                payload_size,
                created_at)
            values (
                @id,
                @subject_id,
                @capability,
                @content_hash,
                @job_id,
                'json',
                @payload_table,
                @payload_id,
                @contract_version,
                @payload_size,
                @created_at)
            on conflict (capability, content_hash) do update
            set job_id = excluded.job_id,
                payload_id = excluded.payload_id,
                contract_version = excluded.contract_version,
                payload_size = excluded.payload_size,
                created_at = excluded.created_at
            returning id;
            """, connection, transaction);
        insertIndex.Parameters.AddWithValue("id", resultIndexId);
        insertIndex.Parameters.AddWithValue("subject_id", command.SubjectId);
        insertIndex.Parameters.AddWithValue("capability", PdfStampRecognitionConstants.Capability);
        insertIndex.Parameters.AddWithValue("content_hash", command.ContentHash);
        insertIndex.Parameters.AddWithValue("job_id", command.JobId);
        insertIndex.Parameters.AddWithValue("payload_table", PayloadTable);
        insertIndex.Parameters.AddWithValue("payload_id", payloadId);
        insertIndex.Parameters.AddWithValue("contract_version", command.ContractVersion);
        insertIndex.Parameters.AddWithValue("payload_size", payloadSize);
        insertIndex.Parameters.AddWithValue("created_at", command.CreatedAt);

        resultIndexId = (Guid)(await insertIndex.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Result index insert did not return id."));

        await using var insertPayload = new NpgsqlCommand("""
            insert into pdf_stamp_recognition_results (
                id,
                result_index_id,
                payload_json,
                contract_version,
                created_at)
            values (
                @id,
                @result_index_id,
                @payload_json,
                @contract_version,
                @created_at);
            """, connection, transaction);
        insertPayload.Parameters.AddWithValue("id", payloadId);
        insertPayload.Parameters.AddWithValue("result_index_id", resultIndexId);
        insertPayload.Parameters.AddWithValue("payload_json", NpgsqlDbType.Jsonb, command.PayloadJson);
        insertPayload.Parameters.AddWithValue("contract_version", command.ContractVersion);
        insertPayload.Parameters.AddWithValue("created_at", command.CreatedAt);
        await insertPayload.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new PdfStampRecognitionResult(
            resultIndexId,
            payloadId,
            command.SubjectId,
            command.JobId,
            command.ContentHash,
            command.PayloadJson,
            command.ContractVersion,
            payloadSize,
            command.CreatedAt);
    }

    public async Task<PdfStampRecognitionResult?> GetByHashAsync(string contentHash, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                i.id,
                p.id,
                i.subject_id,
                i.job_id,
                i.content_hash,
                p.payload_json::text,
                i.contract_version,
                i.payload_size,
                i.created_at
            from processing_result_index i
            join pdf_stamp_recognition_results p on p.id = i.payload_id
            where i.capability = @capability
              and i.content_hash = @content_hash;
            """, connection);
        command.Parameters.AddWithValue("capability", PdfStampRecognitionConstants.Capability);
        command.Parameters.AddWithValue("content_hash", contentHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PdfStampRecognitionResult(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt64(7),
            reader.GetFieldValue<DateTimeOffset>(8));
    }
}
