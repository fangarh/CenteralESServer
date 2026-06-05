using CenteralES.Processing;

namespace CenteralES.Admin;

public sealed record AdminProcessorEndpointListItem(
    Guid? Id,
    string ProcessorKey,
    string Capability,
    string Endpoint,
    bool Enabled,
    int ConcurrencyLimit,
    int Priority,
    string Source,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? DisabledAt);

public sealed record AdminCreateProcessorEndpointCommand(
    string ProcessorKey,
    string Capability,
    string Endpoint,
    int ConcurrencyLimit,
    int Priority,
    Guid ActorAdminId,
    string ActorLogin,
    DateTimeOffset RequestedAt,
    string? Comment,
    string? IpAddress,
    string? UserAgent);

public sealed record AdminUpdateProcessorEndpointCommand(
    Guid EndpointId,
    string ProcessorKey,
    bool? Enabled,
    int? ConcurrencyLimit,
    int? Priority,
    Guid ActorAdminId,
    string ActorLogin,
    DateTimeOffset RequestedAt,
    string? Comment,
    string? IpAddress,
    string? UserAgent);

public abstract record AdminCreateProcessorEndpointResult;

public sealed record AdminCreateProcessorEndpointSuccess(
    AdminProcessorEndpointListItem Endpoint,
    Guid AuditId) : AdminCreateProcessorEndpointResult;

public sealed record AdminCreateProcessorEndpointConflict : AdminCreateProcessorEndpointResult;

public abstract record AdminUpdateProcessorEndpointResult;

public sealed record AdminUpdateProcessorEndpointSuccess(
    AdminProcessorEndpointListItem Endpoint,
    Guid AuditId) : AdminUpdateProcessorEndpointResult;

public sealed record AdminUpdateProcessorEndpointNotFound : AdminUpdateProcessorEndpointResult;

public interface IAdminProcessorEndpointStore
{
    Task<IReadOnlyList<AdminProcessorEndpointListItem>> ListDbEndpointsAsync(
        string processorKey,
        string capability,
        CancellationToken cancellationToken);

    Task<AdminCreateProcessorEndpointResult> CreateAsync(
        AdminCreateProcessorEndpointCommand command,
        CancellationToken cancellationToken);

    Task<AdminUpdateProcessorEndpointResult> UpdateAsync(
        AdminUpdateProcessorEndpointCommand command,
        CancellationToken cancellationToken);
}
