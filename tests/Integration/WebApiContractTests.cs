using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CenteralES.IntegrationTests;

public sealed class WebApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebApiContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Live_health_returns_healthy_status()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var response = await client.GetFromJsonAsync<JsonElement>("/health/live");

        Assert.Equal("healthy", response.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_health_checks_postgres_and_temporary_storage()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var checks = payload.GetProperty("checks").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", payload.GetProperty("status").GetString());
        Assert.Contains(checks, check =>
            check.GetProperty("name").GetString() == "postgres"
            && check.GetProperty("status").GetString() == "healthy");
        Assert.Contains(checks, check =>
            check.GetProperty("name").GetString() == "processingSchema"
            && check.GetProperty("status").GetString() == "healthy");
        Assert.Contains(checks, check =>
            check.GetProperty("name").GetString() == "temporaryStorage"
            && check.GetProperty("status").GetString() == "healthy");
    }

    [Fact]
    public async Task Result_lookup_returns_404_when_hash_is_unknown()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/pdf-stamp-recognition/results/sha256:missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_pdf_job_accepts_multipart_file_and_returns_queued_job()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"%PDF-1.7 fake test pdf {Guid.NewGuid():N}"));
        content.Add(file, "file", "test.pdf");

        var response = await client.PostAsync("/api/pdf-stamp-recognition/jobs", content);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("queued", payload.GetProperty("status").GetString());
        Assert.Equal(1, payload.GetProperty("attemptNumber").GetInt32());
        Assert.StartsWith("sha256:", payload.GetProperty("hash").GetString());
    }

    [Fact]
    public async Task Create_pdf_job_rejects_file_larger_than_configured_limit()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var previous = Environment.GetEnvironmentVariable("PdfStampRecognition__MaxUploadBytes");
        Environment.SetEnvironmentVariable("PdfStampRecognition__MaxUploadBytes", "10");

        try
        {
            using var factory = new WebApplicationFactory<Program>();
            var client = factory.CreateClient();
            using var content = new MultipartFormDataContent();
            using var file = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.7 larger than ten bytes"));
            content.Add(file, "file", "large.pdf");

            var response = await client.PostAsync("/api/pdf-stamp-recognition/jobs", content);
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Equal("payload_too_large", payload.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PdfStampRecognition__MaxUploadBytes", previous);
        }
    }

    [Fact]
    public async Task Active_pdf_job_can_be_observed_by_hash_and_job_id()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"%PDF-1.7 active job {Guid.NewGuid():N}"));
        content.Add(file, "file", "active.pdf");

        var created = await client.PostAsync("/api/pdf-stamp-recognition/jobs", content);
        var createdPayload = await created.Content.ReadFromJsonAsync<JsonElement>();
        var hash = createdPayload.GetProperty("hash").GetString();
        var jobId = createdPayload.GetProperty("jobId").GetString();

        var byHash = await client.GetAsync($"/api/pdf-stamp-recognition/results/{hash}");
        var byHashPayload = await byHash.Content.ReadFromJsonAsync<JsonElement>();
        var byJob = await client.GetAsync($"/api/jobs/{jobId}");
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

    [Fact]
    public async Task Admin_jobs_api_lists_and_returns_job_details()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"%PDF-1.7 admin job {Guid.NewGuid():N}"));
        content.Add(file, "file", "admin.pdf");

        var created = await client.PostAsync("/api/pdf-stamp-recognition/jobs", content);
        var createdPayload = await created.Content.ReadFromJsonAsync<JsonElement>();
        var hash = createdPayload.GetProperty("hash").GetString();
        var jobId = createdPayload.GetProperty("jobId").GetString();

        var list = await client.GetAsync($"/api/admin/jobs?hash={Uri.EscapeDataString(hash!)}");
        var details = await client.GetAsync($"/api/admin/jobs/{jobId}");

        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        Assert.True(
            list.StatusCode == HttpStatusCode.OK,
            $"Expected admin job list to return OK, got {list.StatusCode}: {await list.Content.ReadAsStringAsync()}");
        var listPayload = await list.Content.ReadFromJsonAsync<JsonElement>();
        var jobs = listPayload.GetProperty("jobs").EnumerateArray().ToArray();
        Assert.Single(jobs);
        Assert.Equal(jobId, jobs[0].GetProperty("jobId").GetString());
        Assert.Equal(hash, jobs[0].GetProperty("hash").GetString());
        Assert.Equal("queued", jobs[0].GetProperty("status").GetString());

        Assert.True(
            details.StatusCode == HttpStatusCode.OK,
            $"Expected admin job details to return OK, got {details.StatusCode}: {await details.Content.ReadAsStringAsync()}");
        var detailsPayload = await details.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(jobId, detailsPayload.GetProperty("jobId").GetString());
        Assert.Equal(hash, detailsPayload.GetProperty("hash").GetString());
        Assert.Equal("queued", detailsPayload.GetProperty("status").GetString());
        Assert.True(detailsPayload.TryGetProperty("diagnostics", out _));
        var attempts = detailsPayload.GetProperty("attempts").EnumerateArray().ToArray();
        Assert.Single(attempts);
        Assert.Equal(jobId, attempts[0].GetProperty("jobId").GetString());
        Assert.Equal(1, attempts[0].GetProperty("attemptNumber").GetInt32());
    }

    [Fact]
    public async Task Admin_processor_status_returns_queue_counts_without_external_health_call()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"%PDF-1.7 processor status {Guid.NewGuid():N}"));
        content.Add(file, "file", "processor-status.pdf");

        var created = await client.PostAsync("/api/pdf-stamp-recognition/jobs", content);
        var status = await client.GetAsync("/api/admin/processors/pdf2txt-http-recognizer");

        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);

        var payload = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pdf2txt-http-recognizer", payload.GetProperty("processorKey").GetString());
        Assert.Equal("pdf-stamp-recognition", payload.GetProperty("capability").GetString());
        Assert.Contains(
            payload.GetProperty("health").GetString(),
            new[] { "unknown", "healthy", "unhealthy" });
        Assert.True(payload.GetProperty("queue").GetProperty("queued").GetInt32() >= 1);
        Assert.True(payload.TryGetProperty("workers", out _));
        Assert.True(payload.TryGetProperty("recentDiagnostics", out _));
    }

    private static bool HasConfiguredTestDatabase()
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString();
        if (connectionString is null)
        {
            return false;
        }

        Environment.SetEnvironmentVariable("CENTERALES_PROCESSING_DATABASE", connectionString);
        return true;
    }
}
