using CenteralES.Admin;

namespace CenteralES.Infrastructure.Processing;

public sealed class PostgresAdminProcessingReadStore : IAdminProcessingReadStore
{
    private readonly IAdminJobReadStore _jobs;
    private readonly IAdminProcessorReadStore _processors;
    private readonly IAdminAuditReadStore _audit;
    private readonly IAdminResultReadStore _results;

    public PostgresAdminProcessingReadStore(
        IAdminJobReadStore jobs,
        IAdminProcessorReadStore processors,
        IAdminAuditReadStore audit,
        IAdminResultReadStore results)
    {
        _jobs = jobs;
        _processors = processors;
        _audit = audit;
        _results = results;
    }

    public Task<IReadOnlyList<AdminProcessingJobListItem>> ListJobsAsync(
        AdminProcessingJobListQuery query,
        CancellationToken cancellationToken)
    {
        return _jobs.ListJobsAsync(query, cancellationToken);
    }

    public Task<AdminProcessingJobDetails?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        return _jobs.GetJobAsync(jobId, cancellationToken);
    }

    public Task<AdminJobSupportReport?> GetJobSupportReportAsync(
        Guid jobId,
        string processorKey,
        CancellationToken cancellationToken)
    {
        return _jobs.GetJobSupportReportAsync(jobId, processorKey, cancellationToken);
    }

    public Task<AdminProcessorStatus> GetProcessorStatusAsync(
        string processorKey,
        string capability,
        int recentDiagnosticsLimit,
        CancellationToken cancellationToken)
    {
        return _processors.GetProcessorStatusAsync(processorKey, capability, recentDiagnosticsLimit, cancellationToken);
    }

    public Task<IReadOnlyList<AdminAuditEventListItem>> ListAuditEventsAsync(
        AdminAuditEventListQuery query,
        CancellationToken cancellationToken)
    {
        return _audit.ListAuditEventsAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<AdminResultReference>> ListResultsAsync(
        AdminResultListQuery query,
        CancellationToken cancellationToken)
    {
        return _results.ListResultsAsync(query, cancellationToken);
    }

    public Task<AdminResultDetails?> GetResultAsync(Guid resultIndexId, CancellationToken cancellationToken)
    {
        return _results.GetResultAsync(resultIndexId, cancellationToken);
    }

    public Task<AdminPdfStampRecognitionPayload?> GetPdfStampRecognitionPayloadAsync(
        Guid payloadId,
        CancellationToken cancellationToken)
    {
        return _results.GetPdfStampRecognitionPayloadAsync(payloadId, cancellationToken);
    }
}
