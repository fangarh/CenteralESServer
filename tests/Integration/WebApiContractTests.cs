using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CenteralES.IntegrationTests;

public sealed class WebApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public WebApiContractTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Live_health_returns_healthy_status()
    {
        var response = await _client.GetFromJsonAsync<JsonElement>("/health/live");

        Assert.Equal("healthy", response.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Result_lookup_returns_404_when_hash_is_unknown()
    {
        var response = await _client.GetAsync("/api/pdf-stamp-recognition/results/sha256:missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_pdf_job_accepts_multipart_file_and_returns_queued_job()
    {
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.7 fake test pdf"));
        content.Add(file, "file", "test.pdf");

        var response = await _client.PostAsync("/api/pdf-stamp-recognition/jobs", content);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("queued", payload.GetProperty("status").GetString());
        Assert.Equal(1, payload.GetProperty("attemptNumber").GetInt32());
        Assert.StartsWith("sha256:", payload.GetProperty("hash").GetString());
    }

    [Fact]
    public async Task Active_pdf_job_can_be_observed_by_hash_and_job_id()
    {
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"%PDF-1.7 active job {Guid.NewGuid():N}"));
        content.Add(file, "file", "active.pdf");

        var created = await _client.PostAsync("/api/pdf-stamp-recognition/jobs", content);
        var createdPayload = await created.Content.ReadFromJsonAsync<JsonElement>();
        var hash = createdPayload.GetProperty("hash").GetString();
        var jobId = createdPayload.GetProperty("jobId").GetString();

        var byHash = await _client.GetAsync($"/api/pdf-stamp-recognition/results/{hash}");
        var byHashPayload = await byHash.Content.ReadFromJsonAsync<JsonElement>();
        var byJob = await _client.GetAsync($"/api/jobs/{jobId}");
        var byJobPayload = await byJob.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, byHash.StatusCode);
        Assert.Equal(hash, byHashPayload.GetProperty("hash").GetString());
        Assert.Equal(jobId, byHashPayload.GetProperty("jobId").GetString());
        Assert.Equal("queued", byHashPayload.GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, byJob.StatusCode);
        Assert.Equal(hash, byJobPayload.GetProperty("hash").GetString());
        Assert.Equal(jobId, byJobPayload.GetProperty("jobId").GetString());
        Assert.Equal("queued", byJobPayload.GetProperty("status").GetString());
        Assert.Equal(1, byJobPayload.GetProperty("attemptNumber").GetInt32());
    }
}
