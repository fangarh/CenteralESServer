using CenteralES.Processing;

internal static class AdminProcessorConfiguration
{
    public static IReadOnlyList<string> GetSanitizedEndpointPool(IConfiguration configuration)
    {
        return GetEndpointPool(configuration)
            .Select(SanitizeEndpoint)
            .ToArray();
    }

    public static IReadOnlyList<string> GetEndpointPool(IConfiguration configuration)
    {
        return configuration
            .GetSection("PdfStampRecognition:Processor:endpointPool")
            .GetChildren()
            .Select(value => value.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    public static string SanitizeEndpoint(string endpoint)
    {
        try
        {
            return ProcessorEndpointNormalizer.Normalize(endpoint);
        }
        catch (InvalidOperationException)
        {
            return "invalid-endpoint";
        }
    }
}
