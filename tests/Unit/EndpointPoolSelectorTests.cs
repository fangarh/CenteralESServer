using CenteralES.Processing;

namespace CenteralES.UnitTests;

public sealed class EndpointPoolSelectorTests
{
    [Fact]
    public void SelectLeastInFlight_returns_enabled_healthy_endpoint_with_lowest_load()
    {
        var endpoints = new[]
        {
            new ProcessorEndpointSnapshot("https://pdf2txt-1.local/recognize_json/", true, ProcessorEndpointHealth.Healthy, 2, 3),
            new ProcessorEndpointSnapshot("https://pdf2txt-2.local/recognize_json/", true, ProcessorEndpointHealth.Unknown, 0, 2),
            new ProcessorEndpointSnapshot("https://pdf2txt-3.local/recognize_json/", true, ProcessorEndpointHealth.Healthy, 1, 2)
        };

        var selected = EndpointPoolSelector.SelectLeastInFlight(endpoints);

        Assert.NotNull(selected);
        Assert.Equal("https://pdf2txt-2.local/recognize_json/", selected.Url);
    }

    [Fact]
    public void SelectLeastInFlight_ignores_disabled_unhealthy_and_full_endpoints()
    {
        var endpoints = new[]
        {
            new ProcessorEndpointSnapshot("https://disabled.local/recognize_json/", false, ProcessorEndpointHealth.Healthy, 0, 2),
            new ProcessorEndpointSnapshot("https://unhealthy.local/recognize_json/", true, ProcessorEndpointHealth.Unhealthy, 0, 2),
            new ProcessorEndpointSnapshot("https://full.local/recognize_json/", true, ProcessorEndpointHealth.Healthy, 2, 2)
        };

        var selected = EndpointPoolSelector.SelectLeastInFlight(endpoints);

        Assert.Null(selected);
    }
}
