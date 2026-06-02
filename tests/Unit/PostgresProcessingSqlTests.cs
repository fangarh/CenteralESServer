using CenteralES.Infrastructure.Processing;
using CenteralES.Infrastructure.Postgres;

namespace CenteralES.UnitTests;

public sealed class PostgresProcessingSqlTests
{
    private static string BaselineSchema => PostgresMigrationCatalog.Migrations
        .Single(migration => migration.Id == "0001_processing_baseline")
        .Sql;

    [Fact]
    public void ClaimNext_uses_skip_locked_for_concurrent_workers()
    {
        Assert.Contains("for update skip locked", PostgresProcessingSql.ClaimNext, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schema_keeps_result_index_separate_from_attempt_diagnostics()
    {
        Assert.Contains("create table if not exists processing_result_index", BaselineSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table if not exists processing_attempt_diagnostics", BaselineSchema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schema_contains_worker_heartbeat_table_for_admin_status()
    {
        Assert.Contains("create table if not exists processing_worker_heartbeats", BaselineSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix_processing_worker_heartbeats_processor", BaselineSchema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schema_contains_client_applications_for_api_key_auth()
    {
        Assert.Contains("create table if not exists client_applications", BaselineSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secret_hash text not null", BaselineSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allowed_capabilities text[] not null", BaselineSchema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schema_contains_admin_users_and_sessions_for_cookie_auth()
    {
        Assert.Contains("create table if not exists admin_users", BaselineSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("password_hash text not null", BaselineSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table if not exists admin_sessions", BaselineSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("session_token_hash text not null unique", BaselineSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("csrf_token_hash text not null", BaselineSchema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schema_contains_append_only_admin_audit_events()
    {
        Assert.Contains("create table if not exists admin_audit_events", BaselineSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("action text not null", BaselineSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target_type text not null", BaselineSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("old_value_json jsonb null", BaselineSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new_value_json jsonb null", BaselineSchema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_catalog_contains_baseline_schema()
    {
        var baseline = Assert.Single(
            PostgresMigrationCatalog.Migrations,
            migration => migration.Id == "0001_processing_baseline");

        Assert.Equal("0001_processing_baseline", baseline.Id);
        Assert.Contains("create table if not exists schema_migrations", baseline.Sql, StringComparison.OrdinalIgnoreCase);
    }
}
