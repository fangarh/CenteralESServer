using CenteralES.PdfStampRecognition;
using CenteralES.Processing;
using CenteralES.Processing.Workers;

namespace CenteralES.Worker;

public sealed class PdfStampRecognitionEndpointConfigurationRefresher : IWorkerEndpointConfigurationRefresher
{
    private readonly IProcessorEndpointConfigurationStore _store;
    private readonly HttpPdfStampRecognizerEndpointPool _pool;
    private readonly HttpPdfStampRecognizerOptions _options;

    public PdfStampRecognitionEndpointConfigurationRefresher(
        IProcessorEndpointConfigurationStore store,
        HttpPdfStampRecognizerEndpointPool pool,
        HttpPdfStampRecognizerOptions options)
    {
        _store = store;
        _pool = pool;
        _options = options;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var databaseEndpoints = await _store.ListProcessorEndpointsAsync(
            PdfStampRecognitionConstants.ProcessorKey,
            PdfStampRecognitionConstants.Capability,
            cancellationToken);
        var effective = ProcessorEndpointConfigurationMerger.MergeEnvAndDatabaseEndpoints(
            _options.EndpointPool,
            _options.EndpointConcurrencyLimit,
            databaseEndpoints);

        _pool.UpdateEndpoints(effective);
    }
}
