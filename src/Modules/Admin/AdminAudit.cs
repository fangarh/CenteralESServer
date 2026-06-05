namespace CenteralES.Admin;

public static class AdminAuditActions
{
    public const string ManualRetryJob = "manual_retry_job";
    public const string CreateApiKey = "create_api_key";
    public const string DisableApiKey = "disable_api_key";
    public const string CreateAdminUser = "create_admin_user";
    public const string BootstrapAdminUser = "bootstrap_admin_user";
    public const string DisableAdminUser = "disable_admin_user";
    public const string ChangeAdminPassword = "change_admin_password";
    public const string CreateProcessorEndpoint = "create_processor_endpoint";
    public const string UpdateProcessorEndpoint = "update_processor_endpoint";
}

public static class AdminAuditTargetTypes
{
    public const string ProcessingJob = "processing_job";
    public const string ApiKey = "api_key";
    public const string AdminUser = "admin_user";
    public const string ProcessorEndpoint = "processor_endpoint";
}

public sealed record AdminAuditEventListQuery(
    string? Action,
    string? TargetType,
    string? TargetId,
    string? ActorLogin,
    DateTimeOffset? OccurredFrom,
    DateTimeOffset? OccurredTo,
    int Limit);

public sealed record AdminAuditEventListItem(
    Guid AuditId,
    DateTimeOffset OccurredAt,
    Guid? ActorAdminId,
    string? ActorLogin,
    string Action,
    string TargetType,
    string TargetId,
    string? Comment,
    string CorrelationId);
