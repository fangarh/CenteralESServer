using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CenteralES.AccessControl;
using CenteralES.Infrastructure.PdfStampRecognition;
using CenteralES.Infrastructure.Postgres;
using CenteralES.Infrastructure.Processing;
using CenteralES.PdfStampRecognition;
using CenteralES.Processing;
using CenteralES.Processing.Queue;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace CenteralES.IntegrationTests;

public sealed class WebApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebApiContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Admin_ui_is_served_by_web_application()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/admin");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Centeral ES Admin", html, StringComparison.Ordinal);
        Assert.Contains("/admin/app.css?v=", html, StringComparison.Ordinal);
        Assert.Contains("/admin/app.js?v=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_ui_assets_are_served_with_static_content_types()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var cssResponse = await client.GetAsync("/admin/app.css");
        var jsResponse = await client.GetAsync("/admin/app.js");
        var css = await cssResponse.Content.ReadAsStringAsync();
        var js = await jsResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, cssResponse.StatusCode);
        Assert.Equal("text/css", cssResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains(":root", css, StringComparison.Ordinal);
        Assert.DoesNotContain("<!doctype html>", css, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, jsResponse.StatusCode);
        Assert.Equal("text/javascript", jsResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("const state", js, StringComparison.Ordinal);
        Assert.DoesNotContain("<!doctype html>", js, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_ui_contains_job_details_surface()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/admin");
        var js = await client.GetStringAsync("/admin/app.js?v=20260602-10");

        Assert.Contains("job-details-panel", html, StringComparison.Ordinal);
        Assert.Contains("support-report-button", html, StringComparison.Ordinal);
        Assert.Contains("loadJobDetails", js, StringComparison.Ordinal);
        Assert.Contains("/support-report", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_ui_contains_processor_details_surface()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/admin");
        var js = await client.GetStringAsync("/admin/app.js?v=20260602-10");

        Assert.Contains("processors-tab", html, StringComparison.Ordinal);
        Assert.Contains("processor-workers-body", html, StringComparison.Ordinal);
        Assert.Contains("processor-diagnostics-body", html, StringComparison.Ordinal);
        Assert.Contains("renderProcessorDetails", js, StringComparison.Ordinal);
        Assert.Contains("/api/admin/processors/pdf2txt-http-recognizer", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_ui_contains_health_surface()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/admin");
        var js = await client.GetStringAsync("/admin/app.js?v=20260602-10");

        Assert.Contains("health-tab", html, StringComparison.Ordinal);
        Assert.Contains("health-checks-body", html, StringComparison.Ordinal);
        Assert.Contains("renderHealthDetails", js, StringComparison.Ordinal);
        Assert.Contains("/health/live", js, StringComparison.Ordinal);
        Assert.Contains("/health/ready", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_ui_contains_delivery_surface()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/admin");
        var js = await client.GetStringAsync("/admin/app.js?v=20260602-10");

        Assert.Contains("delivery-tab", html, StringComparison.Ordinal);
        Assert.Contains("delivery-components-body", html, StringComparison.Ordinal);
        Assert.Contains("delivery-runtime-list", html, StringComparison.Ordinal);
        Assert.Contains("renderDeliveryDetails", js, StringComparison.Ordinal);
        Assert.Contains("pdf2txt-http-recognizer", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_ui_contains_storage_surface()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/admin");
        var js = await client.GetStringAsync("/admin/app.js?v=20260602-10");

        Assert.Contains("storage-tab", html, StringComparison.Ordinal);
        Assert.Contains("storage-summary-list", html, StringComparison.Ordinal);
        Assert.Contains("storage-capacity-list", html, StringComparison.Ordinal);
        Assert.Contains("renderStorageDetails", js, StringComparison.Ordinal);
        Assert.Contains("/api/admin/storage", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_ui_contains_results_surface()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/admin");
        var js = await client.GetStringAsync("/admin/app.js?v=20260602-10");

        Assert.Contains("results-tab", html, StringComparison.Ordinal);
        Assert.Contains("results-body", html, StringComparison.Ordinal);
        Assert.Contains("result-details-panel", html, StringComparison.Ordinal);
        Assert.Contains("renderResults", js, StringComparison.Ordinal);
        Assert.Contains("/api/admin/results", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_ui_contains_settings_surface()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/admin");
        var js = await client.GetStringAsync("/admin/app.js?v=20260602-10");

        Assert.Contains("settings-tab", html, StringComparison.Ordinal);
        Assert.Contains("settings-summary-list", html, StringComparison.Ordinal);
        Assert.Contains("settings-processor-list", html, StringComparison.Ordinal);
        Assert.Contains("renderSettingsDetails", js, StringComparison.Ordinal);
        Assert.Contains("/api/admin/settings", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_ui_contains_audit_filter_surface()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/admin");
        var js = await client.GetStringAsync("/admin/app.js?v=20260602-10");

        Assert.Contains("audit-filter-form", html, StringComparison.Ordinal);
        Assert.Contains("audit-action-filter", html, StringComparison.Ordinal);
        Assert.Contains("audit-target-type-filter", html, StringComparison.Ordinal);
        Assert.Contains("audit-count", html, StringComparison.Ordinal);
        Assert.Contains("buildAuditQuery", js, StringComparison.Ordinal);
        Assert.Contains("renderAuditFilters", js, StringComparison.Ordinal);
        Assert.Contains("audit-safe-details", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_settings_requires_admin_session()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/settings");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", payload.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Admin_settings_returns_safe_runtime_configuration()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var admin = await CreateAdminClientAsync(_factory);
        var response = await admin.Client.GetAsync("/api/admin/settings");
        var body = await response.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(262144000, payload.GetProperty("publicApi").GetProperty("maxUploadBytes").GetInt64());
        Assert.True(payload.GetProperty("storage").TryGetProperty("temporaryRootPath", out _));
        Assert.Equal("pdf2txt-http-recognizer", payload.GetProperty("processor").GetProperty("processorKey").GetString());
        Assert.Equal("pdf-stamp-recognition", payload.GetProperty("processor").GetProperty("capability").GetString());
        Assert.True(payload.GetProperty("processor").TryGetProperty("endpointCount", out _));
        Assert.True(payload.GetProperty("processor").TryGetProperty("maxAttempts", out _));
        Assert.True(payload.GetProperty("processor").TryGetProperty("processorOverloadedDelay", out _));
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connectionString", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CENTERALES_PROCESSING_DATABASE", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_results_requires_admin_session()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/results");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", payload.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Admin_results_list_returns_references_without_payload()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var completed = await CreateCompletedResultAsync();
        var admin = await CreateAdminClientAsync(_factory);
        var response = await admin.Client.GetAsync($"/api/admin/results?hash={Uri.EscapeDataString(completed.Hash)}");
        var body = await response.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        var results = payload.GetProperty("results").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(results);
        Assert.Equal(completed.ResultIndexId.ToString("N"), results[0].GetProperty("resultIndexId").GetString());
        Assert.Equal(completed.JobId.ToString("N"), results[0].GetProperty("jobId").GetString());
        Assert.Equal(completed.Hash, results[0].GetProperty("hash").GetString());
        Assert.Equal("pdf-stamp-recognition", results[0].GetProperty("capability").GetString());
        Assert.Equal("json", results[0].GetProperty("resultKind").GetString());
        Assert.Equal("pdf_stamp_recognition_results", results[0].GetProperty("payloadTable").GetString());
        Assert.True(results[0].GetProperty("payloadSize").GetInt64() > 0);
        Assert.DoesNotContain("hidden payload", body, StringComparison.Ordinal);
        Assert.DoesNotContain("payloadJson", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_result_details_returns_metadata_without_payload()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var completed = await CreateCompletedResultAsync();
        var admin = await CreateAdminClientAsync(_factory);
        var response = await admin.Client.GetAsync($"/api/admin/results/{completed.ResultIndexId:N}");
        var body = await response.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(completed.ResultIndexId.ToString("N"), payload.GetProperty("resultIndexId").GetString());
        Assert.Equal(completed.JobId.ToString("N"), payload.GetProperty("jobId").GetString());
        Assert.Equal(completed.Hash, payload.GetProperty("hash").GetString());
        Assert.Equal("test-v1", payload.GetProperty("contractVersion").GetString());
        Assert.False(payload.TryGetProperty("result", out _));
        Assert.DoesNotContain("hidden payload", body, StringComparison.Ordinal);
        Assert.DoesNotContain("payloadJson", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_storage_requires_admin_session()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/storage");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", payload.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Admin_storage_returns_temporary_storage_capacity()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var admin = await CreateAdminClientAsync(_factory);
        var response = await admin.Client.GetAsync("/api/admin/storage");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var temporary = payload.GetProperty("temporary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("local", temporary.GetProperty("provider").GetString());
        Assert.Equal("temporary-input", temporary.GetProperty("purpose").GetString());
        Assert.Contains(
            temporary.GetProperty("status").GetString(),
            new[] { "healthy", "warning", "full" });
        Assert.True(temporary.GetProperty("usedBytes").GetInt64() >= 0);
        Assert.True(temporary.TryGetProperty("rootPath", out _));
        Assert.True(temporary.TryGetProperty("hardLimitBytes", out _));
        Assert.True(temporary.TryGetProperty("softLimitBytes", out _));
        Assert.True(temporary.TryGetProperty("minimumFreeBytes", out _));
        Assert.True(temporary.TryGetProperty("availableFreeBytes", out _));
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

        var client = await CreateAuthorizedClientAsync(_factory);
        var response = await client.GetAsync("/api/pdf-stamp-recognition/results/sha256:missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Public_api_returns_401_when_api_key_is_missing()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/pdf-stamp-recognition/results/sha256:missing");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", payload.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Public_api_returns_403_when_api_key_lacks_capability()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = await CreateAuthorizedClientAsync(_factory, "other-capability");
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"%PDF-1.7 forbidden {Guid.NewGuid():N}"));
        content.Add(file, "file", "forbidden.pdf");

        var response = await client.PostAsync("/api/pdf-stamp-recognition/jobs", content);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", payload.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_pdf_job_accepts_multipart_file_and_returns_queued_job()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = await CreateAuthorizedClientAsync(_factory);
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
            var client = await CreateAuthorizedClientAsync(factory);
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
    public async Task Create_pdf_job_returns_503_when_temporary_storage_hard_limit_is_exceeded()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var previousHardLimit = Environment.GetEnvironmentVariable("Storage__TemporaryHardLimitBytes");
        var previousTemporaryRoot = Environment.GetEnvironmentVariable("Storage__TemporaryRoot");
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"centerales-hard-limit-it-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("Storage__TemporaryHardLimitBytes", "10");
        Environment.SetEnvironmentVariable("Storage__TemporaryRoot", temporaryRoot);

        try
        {
            using var factory = new WebApplicationFactory<Program>();
            var client = await CreateAuthorizedClientAsync(factory);
            using var content = new MultipartFormDataContent();
            using var file = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.7 temporary storage hard limit"));
            content.Add(file, "file", "storage-full.pdf");

            var response = await client.PostAsync("/api/pdf-stamp-recognition/jobs", content);
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("temporary_storage_full", payload.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("Storage__TemporaryHardLimitBytes", previousHardLimit);
            Environment.SetEnvironmentVariable("Storage__TemporaryRoot", previousTemporaryRoot);
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Active_pdf_job_can_be_observed_by_hash_and_job_id()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = await CreateAuthorizedClientAsync(_factory);
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
    public async Task Admin_api_requires_session_cookie()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/jobs");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", payload.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Admin_login_sets_session_cookie_and_logout_requires_csrf()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var admin = await CreateAdminClientAsync(_factory);
        var me = await admin.Client.GetAsync("/api/admin/auth/me");
        var logoutWithoutCsrf = await admin.Client.PostAsync("/api/admin/auth/logout", null);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auth/logout");
        logoutRequest.Headers.Add("X-CSRF-Token", admin.CsrfToken);
        var logout = await admin.Client.SendAsync(logoutRequest);
        var afterLogout = await admin.Client.GetAsync("/api/admin/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var mePayload = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(admin.Login, mePayload.GetProperty("admin").GetProperty("login").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, logoutWithoutCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Admin_jobs_api_lists_and_returns_job_details()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = await CreateAuthorizedClientAsync(_factory);
        var admin = await CreateAdminClientAsync(_factory);
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"%PDF-1.7 admin job {Guid.NewGuid():N}"));
        content.Add(file, "file", "admin.pdf");

        var created = await client.PostAsync("/api/pdf-stamp-recognition/jobs", content);
        var createdPayload = await created.Content.ReadFromJsonAsync<JsonElement>();
        var hash = createdPayload.GetProperty("hash").GetString();
        var jobId = createdPayload.GetProperty("jobId").GetString();

        var list = await admin.Client.GetAsync($"/api/admin/jobs?hash={Uri.EscapeDataString(hash!)}");
        var details = await admin.Client.GetAsync($"/api/admin/jobs/{jobId}");

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
    public async Task Admin_job_support_report_returns_sanitized_context()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var admin = await CreateAdminClientAsync(_factory);
        var blocked = await CreateBlockedProcessingJobAsync();
        using var retryRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/jobs/{blocked.JobId:N}/retry");
        retryRequest.Headers.Add("X-CSRF-Token", admin.CsrfToken);
        retryRequest.Content = JsonContent.Create(new { Comment = "retry from support report test" });

        var retry = await admin.Client.SendAsync(retryRequest);
        var report = await admin.Client.GetAsync($"/api/admin/jobs/{blocked.JobId:N}/support-report");

        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);

        var body = await report.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(body);

        Assert.Equal(blocked.JobId.ToString("N"), payload.GetProperty("jobId").GetString());
        Assert.Equal(blocked.Hash, payload.GetProperty("hash").GetString());
        Assert.Equal("pdf-stamp-recognition", payload.GetProperty("capability").GetString());
        Assert.Equal("pdf2txt-http-recognizer", payload.GetProperty("processorKey").GetString());
        Assert.Equal("blocked", payload.GetProperty("status").GetString());
        Assert.Equal("Unexpected response.", payload.GetProperty("diagnostics").GetProperty("excerpt").GetString());
        Assert.False(payload.TryGetProperty("temporaryFileKey", out _));
        Assert.False(body.Contains("temp/", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("result").ValueKind);

        var attempts = payload.GetProperty("attempts").EnumerateArray().ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.Equal(1, attempts[0].GetProperty("attemptNumber").GetInt32());
        Assert.Equal(2, attempts[1].GetProperty("attemptNumber").GetInt32());

        var processor = payload.GetProperty("processor");
        Assert.Equal("pdf2txt-http-recognizer", processor.GetProperty("processorKey").GetString());
        Assert.True(processor.TryGetProperty("queue", out _));
        Assert.True(processor.TryGetProperty("workers", out _));
        Assert.True(processor.TryGetProperty("recentDiagnostics", out _));

        var auditEvents = payload.GetProperty("auditEvents").EnumerateArray().ToArray();
        Assert.Single(auditEvents);
        Assert.Equal("manual_retry_job", auditEvents[0].GetProperty("action").GetString());
        Assert.Equal(blocked.JobId.ToString("N"), auditEvents[0].GetProperty("targetId").GetString());
    }

    [Fact]
    public async Task Admin_audit_api_lists_filtered_events_without_raw_values()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var admin = await CreateAdminClientAsync(_factory);
        var blocked = await CreateBlockedProcessingJobAsync();
        var occurredFrom = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"));
        var occurredTo = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"));
        using var retryRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/jobs/{blocked.JobId:N}/retry");
        retryRequest.Headers.Add("X-CSRF-Token", admin.CsrfToken);
        retryRequest.Content = JsonContent.Create(new { Comment = "retry from audit list test" });

        var retry = await admin.Client.SendAsync(retryRequest);
        var audit = await admin.Client.GetAsync(
            $"/api/admin/audit?action=manual_retry_job&targetType=processing_job&targetId={blocked.JobId:N}&actor={Uri.EscapeDataString(admin.Login)}&occurredFrom={occurredFrom}&occurredTo={occurredTo}&limit=5");

        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);

        var body = await audit.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        var events = payload.GetProperty("events").EnumerateArray().ToArray();

        Assert.Single(events);
        Assert.Equal(admin.Login, events[0].GetProperty("actorLogin").GetString());
        Assert.Equal("manual_retry_job", events[0].GetProperty("action").GetString());
        Assert.Equal("processing_job", events[0].GetProperty("targetType").GetString());
        Assert.Equal(blocked.JobId.ToString("N"), events[0].GetProperty("targetId").GetString());
        Assert.Equal("retry from audit list test", events[0].GetProperty("comment").GetString());
        Assert.False(events[0].TryGetProperty("oldValue", out _));
        Assert.False(events[0].TryGetProperty("newValue", out _));
        Assert.False(events[0].TryGetProperty("technicalMetadata", out _));
        Assert.DoesNotContain("old_value_json", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new_value_json", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_api_keys_can_be_created_listed_disabled_and_audited()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var admin = await CreateAdminClientAsync(_factory);
        var keyId = $"adm_{Guid.NewGuid():N}";
        var createWithoutCsrf = await admin.Client.PostAsJsonAsync(
            "/api/admin/api-keys",
            new
            {
                KeyId = keyId,
                Name = "Integration API key",
                AllowedCapabilities = new[] { PdfStampRecognitionConstants.Capability }
            });

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/api-keys");
        createRequest.Headers.Add("X-CSRF-Token", admin.CsrfToken);
        createRequest.Content = JsonContent.Create(new
        {
            KeyId = keyId,
            Name = "Integration API key",
            AllowedCapabilities = new[] { PdfStampRecognitionConstants.Capability }
        });
        var create = await admin.Client.SendAsync(createRequest);

        Assert.Equal(HttpStatusCode.Forbidden, createWithoutCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var secret = created.GetProperty("secret").GetString();

        var listAfterCreate = await admin.Client.GetAsync($"/api/admin/api-keys?keyId={Uri.EscapeDataString(keyId)}");
        var listPayload = await listAfterCreate.Content.ReadFromJsonAsync<JsonElement>();
        var keys = listPayload.GetProperty("keys").EnumerateArray().ToArray();

        var publicClient = _factory.CreateClient();
        publicClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", $"{keyId}.{secret}");
        var authorizedBeforeDisable = await publicClient.GetAsync("/api/pdf-stamp-recognition/results/sha256:missing");

        using var disableRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/api-keys/{keyId}/disable");
        disableRequest.Headers.Add("X-CSRF-Token", admin.CsrfToken);
        disableRequest.Content = JsonContent.Create(new { Comment = "disable from integration test" });
        var disable = await admin.Client.SendAsync(disableRequest);

        var unauthorizedAfterDisable = await publicClient.GetAsync("/api/pdf-stamp-recognition/results/sha256:missing");
        var audit = await admin.Client.GetAsync($"/api/admin/audit?targetType=api_key&targetId={Uri.EscapeDataString(keyId)}&limit=10");
        var auditBody = await audit.Content.ReadAsStringAsync();
        var auditPayload = JsonSerializer.Deserialize<JsonElement>(auditBody);
        var auditEvents = auditPayload.GetProperty("events").EnumerateArray().ToArray();

        Assert.Equal(keyId, created.GetProperty("keyId").GetString());
        Assert.Equal("Integration API key", created.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(secret));
        Assert.False(created.TryGetProperty("secretHash", out _));

        Assert.Equal(HttpStatusCode.OK, listAfterCreate.StatusCode);
        Assert.Single(keys);
        Assert.Equal(keyId, keys[0].GetProperty("keyId").GetString());
        Assert.True(keys[0].GetProperty("isActive").GetBoolean());
        Assert.False(keys[0].TryGetProperty("secret", out _));
        Assert.False(keys[0].TryGetProperty("secretHash", out _));

        Assert.Equal(HttpStatusCode.NotFound, authorizedBeforeDisable.StatusCode);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedAfterDisable.StatusCode);

        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        Assert.Contains(auditEvents, item => item.GetProperty("action").GetString() == "create_api_key");
        Assert.Contains(auditEvents, item => item.GetProperty("action").GetString() == "disable_api_key");
        Assert.DoesNotContain(secret!, auditBody, StringComparison.Ordinal);
        Assert.DoesNotContain("secret_hash", auditBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_users_can_be_created_listed_password_changed_disabled_and_audited()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var admin = await CreateAdminClientAsync(_factory);
        var login = $"managed_{Guid.NewGuid():N}";
        var password = $"Managed_password_{Guid.NewGuid():N}";
        var nextPassword = $"Managed_next_{Guid.NewGuid():N}";

        var createWithoutCsrf = await admin.Client.PostAsJsonAsync(
            "/api/admin/users",
            new
            {
                Login = login,
                Password = password
            });

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users");
        createRequest.Headers.Add("X-CSRF-Token", admin.CsrfToken);
        createRequest.Content = JsonContent.Create(new
        {
            Login = login,
            Password = password
        });
        var create = await admin.Client.SendAsync(createRequest);

        Assert.Equal(HttpStatusCode.Forbidden, createWithoutCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("userId").GetString();
        Assert.Equal(login, created.GetProperty("login").GetString());
        Assert.Equal("admin", created.GetProperty("role").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.False(created.TryGetProperty("password", out _));
        Assert.False(created.TryGetProperty("passwordHash", out _));

        var list = await admin.Client.GetAsync($"/api/admin/users?login={Uri.EscapeDataString(login)}");
        var listPayload = await list.Content.ReadFromJsonAsync<JsonElement>();
        var users = listPayload.GetProperty("users").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Single(users);
        Assert.Equal(userId, users[0].GetProperty("userId").GetString());
        Assert.Equal(login, users[0].GetProperty("login").GetString());
        Assert.False(users[0].TryGetProperty("password", out _));
        Assert.False(users[0].TryGetProperty("passwordHash", out _));

        using var createdAdminClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var createdLogin = await createdAdminClient.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                Login = login,
                Password = password
            });

        using var passwordRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/password");
        passwordRequest.Headers.Add("X-CSRF-Token", admin.CsrfToken);
        passwordRequest.Content = JsonContent.Create(new
        {
            Password = nextPassword,
            Comment = "password change from integration test"
        });
        var passwordChange = await admin.Client.SendAsync(passwordRequest);

        var oldPasswordLogin = await createdAdminClient.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                Login = login,
                Password = password
            });
        var nextPasswordLogin = await createdAdminClient.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                Login = login,
                Password = nextPassword
            });

        using var disableRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/disable");
        disableRequest.Headers.Add("X-CSRF-Token", admin.CsrfToken);
        disableRequest.Content = JsonContent.Create(new { Comment = "disable admin from integration test" });
        var disable = await admin.Client.SendAsync(disableRequest);

        var disabledLogin = await createdAdminClient.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                Login = login,
                Password = nextPassword
            });

        var audit = await admin.Client.GetAsync($"/api/admin/audit?targetType=admin_user&targetId={userId}&limit=10");
        var auditBody = await audit.Content.ReadAsStringAsync();
        var auditPayload = JsonSerializer.Deserialize<JsonElement>(auditBody);
        var auditEvents = auditPayload.GetProperty("events").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, createdLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, passwordChange.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, nextPasswordLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, disabledLogin.StatusCode);

        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        Assert.Contains(auditEvents, item => item.GetProperty("action").GetString() == "create_admin_user");
        Assert.Contains(auditEvents, item => item.GetProperty("action").GetString() == "change_admin_password");
        Assert.Contains(auditEvents, item => item.GetProperty("action").GetString() == "disable_admin_user");
        Assert.DoesNotContain(password, auditBody, StringComparison.Ordinal);
        Assert.DoesNotContain(nextPassword, auditBody, StringComparison.Ordinal);
        Assert.DoesNotContain("password_hash", auditBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_manual_retry_requires_csrf_and_creates_new_queued_attempt()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = await CreateAuthorizedClientAsync(_factory);
        var admin = await CreateAdminClientAsync(_factory);
        var blocked = await CreateBlockedProcessingJobAsync();

        var withoutCsrf = await admin.Client.PostAsJsonAsync(
            $"/api/admin/jobs/{blocked.JobId:N}/retry",
            new { Comment = "retry from test" });

        using var retryRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/jobs/{blocked.JobId:N}/retry");
        retryRequest.Headers.Add("X-CSRF-Token", admin.CsrfToken);
        retryRequest.Content = JsonContent.Create(new { Comment = "retry from test" });
        var retry = await admin.Client.SendAsync(retryRequest);
        var payload = await retry.Content.ReadFromJsonAsync<JsonElement>();
        var newJobId = payload.GetProperty("jobId").GetString();
        var byJob = await client.GetAsync($"/api/jobs/{newJobId}");
        var byJobPayload = await byJob.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Forbidden, withoutCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal(blocked.JobId.ToString("N"), payload.GetProperty("sourceJobId").GetString());
        Assert.Equal(blocked.Hash, payload.GetProperty("hash").GetString());
        Assert.Equal(2, payload.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("queued", payload.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("auditId").GetString()));

        Assert.Equal(HttpStatusCode.OK, byJob.StatusCode);
        Assert.Equal(newJobId, byJobPayload.GetProperty("jobId").GetString());
        Assert.Equal("queued", byJobPayload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Admin_processor_status_returns_queue_counts_without_external_health_call()
    {
        if (!HasConfiguredTestDatabase())
        {
            return;
        }

        var client = await CreateAuthorizedClientAsync(_factory);
        var admin = await CreateAdminClientAsync(_factory);
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"%PDF-1.7 processor status {Guid.NewGuid():N}"));
        content.Add(file, "file", "processor-status.pdf");

        var created = await client.PostAsync("/api/pdf-stamp-recognition/jobs", content);
        var status = await admin.Client.GetAsync("/api/admin/processors/pdf2txt-http-recognizer");

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

    private static async Task<HttpClient> CreateAuthorizedClientAsync(
        WebApplicationFactory<Program> factory,
        params string[] allowedCapabilities)
    {
        var keyId = $"it_{Guid.NewGuid():N}";
        var secret = $"secret_{Guid.NewGuid():N}";
        await SeedApiKeyAsync(
            keyId,
            secret,
            allowedCapabilities.Length == 0
                ? new[] { PdfStampRecognitionConstants.Capability }
                : allowedCapabilities);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", $"{keyId}.{secret}");
        return client;
    }

    private static async Task<AdminTestSession> CreateAdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var login = $"admin_{Guid.NewGuid():N}";
        var password = $"Admin_password_{Guid.NewGuid():N}";
        await SeedAdminUserAsync(login, password);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                Login = login,
                Password = password
            });
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected admin login to return OK, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookieValues));
        Assert.Contains(setCookieValues, value => value.Contains("centerales_admin_session=", StringComparison.Ordinal));

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var csrfToken = payload.GetProperty("csrfToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(csrfToken));

        return new AdminTestSession(client, login, csrfToken!);
    }

    private static async Task SeedApiKeyAsync(
        string keyId,
        string secret,
        IReadOnlyList<string> allowedCapabilities)
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString()
            ?? throw new InvalidOperationException("Test database is not configured.");
        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand("""
            insert into client_applications (
                key_id,
                name,
                secret_hash,
                is_active,
                allowed_capabilities,
                created_at,
                updated_at)
            values (
                @key_id,
                @name,
                @secret_hash,
                true,
                @allowed_capabilities,
                @created_at,
                @created_at);
            """, connection);
        command.Parameters.AddWithValue("key_id", keyId);
        command.Parameters.AddWithValue("name", $"Integration key {keyId}");
        command.Parameters.AddWithValue("secret_hash", ApiKeySecretHasher.HashSecret(secret));
        command.Parameters.AddWithValue("allowed_capabilities", allowedCapabilities.ToArray());
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task SeedAdminUserAsync(string login, string password)
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString()
            ?? throw new InvalidOperationException("Test database is not configured.");
        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand("""
            insert into admin_users (
                id,
                login,
                password_hash,
                is_active,
                role,
                created_at,
                updated_at)
            values (
                @id,
                @login,
                @password_hash,
                true,
                'admin',
                @created_at,
                @created_at);
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("login", login);
        command.Parameters.AddWithValue("password_hash", AdminPasswordHasher.HashPassword(password));
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<BlockedJobFixture> CreateBlockedProcessingJobAsync()
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString()
            ?? throw new InvalidOperationException("Test database is not configured.");
        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await ResetProcessingTablesAsync(dataSource, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var queue = new PostgresProcessingJobQueue(dataSource);
        var hash = $"sha256:{Guid.NewGuid():N}";
        var enqueued = await queue.EnqueueAsync(
            new CreateProcessingJobCommand(
                PdfStampRecognitionConstants.Capability,
                hash,
                $"temp/{Guid.NewGuid():N}.pdf",
                now),
            CancellationToken.None);
        var claimed = await queue.ClaimNextAsync(now.AddSeconds(1), CancellationToken.None)
            ?? throw new InvalidOperationException("Expected test job to be claimable.");

        await queue.FailAsync(
            new FailProcessingJobCommand(
                claimed.JobId,
                claimed.SubjectId,
                NormalizedProcessorError.ProcessorContractError,
                Final: true,
                new AttemptDiagnostics(
                    Endpoint: "https://pdf2txt.local/recognize_json/",
                    Duration: TimeSpan.FromMilliseconds(10),
                    HttpStatus: 200,
                    NormalizedError: NormalizedProcessorError.ProcessorContractError,
                    Retryable: true,
                    CorrelationId: $"corr-{Guid.NewGuid():N}",
                    RawErrorExcerpt: "Unexpected response."),
                now.AddSeconds(2)),
            CancellationToken.None);

        return new BlockedJobFixture(enqueued.JobId, hash);
    }

    private static async Task<CompletedResultFixture> CreateCompletedResultAsync()
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString()
            ?? throw new InvalidOperationException("Test database is not configured.");
        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await ResetProcessingTablesAsync(dataSource, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var queue = new PostgresProcessingJobQueue(dataSource);
        var resultStore = new PostgresPdfStampRecognitionResultStore(dataSource);
        var hash = $"sha256:{Guid.NewGuid():N}";
        var enqueued = await queue.EnqueueAsync(
            new CreateProcessingJobCommand(
                PdfStampRecognitionConstants.Capability,
                hash,
                $"temp/{Guid.NewGuid():N}.pdf",
                now),
            CancellationToken.None);
        var claimed = await queue.ClaimNextAsync(now.AddSeconds(1), CancellationToken.None)
            ?? throw new InvalidOperationException("Expected test job to be claimable.");

        var saved = await resultStore.SaveAsync(
            new SavePdfStampRecognitionResultCommand(
                claimed.SubjectId,
                claimed.JobId,
                claimed.ContentHash,
                """{"source":"admin-results-test","people":[{"name":"hidden payload"}]}""",
                "test-v1",
                now.AddSeconds(2)),
            CancellationToken.None);

        await queue.CompleteAsync(
            new CompleteProcessingJobCommand(
                claimed.JobId,
                claimed.SubjectId,
                saved.ResultIndexId,
                new AttemptDiagnostics(
                    Endpoint: "fake://test",
                    Duration: TimeSpan.FromMilliseconds(10),
                    HttpStatus: 200,
                    NormalizedError: null,
                    Retryable: null,
                    CorrelationId: $"corr-{Guid.NewGuid():N}"),
                now.AddSeconds(3)),
            CancellationToken.None);

        return new CompletedResultFixture(saved.ResultIndexId, enqueued.JobId, hash);
    }

    private static async Task ResetProcessingTablesAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            truncate table
                admin_audit_events,
                processing_worker_heartbeats,
                pdf_stamp_recognition_results,
                processing_attempt_diagnostics,
                processing_result_index,
                processing_jobs,
                processing_subjects
            cascade;
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record AdminTestSession(HttpClient Client, string Login, string CsrfToken);

    private sealed record BlockedJobFixture(Guid JobId, string Hash);

    private sealed record CompletedResultFixture(Guid ResultIndexId, Guid JobId, string Hash);
}
