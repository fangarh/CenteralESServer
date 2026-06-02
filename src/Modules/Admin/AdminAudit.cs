namespace CenteralES.Admin;

public static class AdminAuditActions
{
    public const string ManualRetryJob = "manual_retry_job";
    public const string CreateApiKey = "create_api_key";
    public const string DisableApiKey = "disable_api_key";
}

public static class AdminAuditTargetTypes
{
    public const string ProcessingJob = "processing_job";
    public const string ApiKey = "api_key";
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
