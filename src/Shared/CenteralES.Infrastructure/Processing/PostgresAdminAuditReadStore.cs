using CenteralES.Admin;
using Npgsql;

namespace CenteralES.Infrastructure.Processing;

public sealed class PostgresAdminAuditReadStore : IAdminAuditReadStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAdminAuditReadStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
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
                reader.IsDBNull(7) ? null : PostgresAdminReadStoreHelpers.ToSafeExcerpt(reader.GetString(7)),
                reader.GetString(8)));
        }

        return events;
    }
}
