using CenteralES.Infrastructure.Processing;

namespace CenteralES.UnitTests;

public sealed class PostgresProcessingSqlTests
{
    [Fact]
    public void ClaimNext_uses_skip_locked_for_concurrent_workers()
    {
        Assert.Contains("for update skip locked", PostgresProcessingSql.ClaimNext, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schema_keeps_result_index_separate_from_attempt_diagnostics()
    {
        Assert.Contains("create table if not exists processing_result_index", PostgresProcessingSql.Schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table if not exists processing_attempt_diagnostics", PostgresProcessingSql.Schema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schema_contains_worker_heartbeat_table_for_admin_status()
    {
        Assert.Contains("create table if not exists processing_worker_heartbeats", PostgresProcessingSql.Schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix_processing_worker_heartbeats_processor", PostgresProcessingSql.Schema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schema_contains_client_applications_for_api_key_auth()
    {
        Assert.Contains("create table if not exists client_applications", PostgresProcessingSql.Schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secret_hash text not null", PostgresProcessingSql.Schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allowed_capabilities text[] not null", PostgresProcessingSql.Schema, StringComparison.OrdinalIgnoreCase);
    }
}
