using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CenteralES.Admin.Bootstrap.WinForms;

internal sealed class MvpHttpTestClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MvpHttpTestClient(Uri baseUri)
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<string> LoginAsync(string login, string password, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/api/admin/auth/login",
            new { Login = login, Password = password },
            JsonOptions,
            cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Admin login failed: {ReadError(document)}");
        }

        _ = ReadRequiredString(document.RootElement, "csrfToken");
        var adminLogin = ReadString(document.RootElement.GetProperty("admin"), "login");
        return adminLogin ?? login;
    }

    public async Task<IReadOnlyList<MvpServiceDescriptor>> DiscoverServicesAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("/api/admin/services", cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Service discovery failed: {ReadError(document)}");
        }

        return document.RootElement
            .GetProperty("services")
            .EnumerateArray()
            .Select(service => new MvpServiceDescriptor(
                ReadRequiredString(service, "capability"),
                ReadRequiredString(service, "processorKey"),
                ReadRequiredString(service, "recognizer"),
                ReadInt(service, "endpointCount"),
                ReadRequiredString(service, "contractVersion")))
            .ToArray();
    }

    public async Task<IReadOnlyList<MvpServiceTestResult>> TestServiceAsync(
        MvpServiceDescriptor service,
        string? apiKeyCredential,
        string? pdfPath,
        CancellationToken cancellationToken)
    {
        var results = new List<MvpServiceTestResult>();

        results.Add(await TestHealthEndpointAsync("/health/live", "Web live", cancellationToken));
        results.Add(await TestHealthEndpointAsync("/health/ready", "Web ready", cancellationToken));
        results.Add(await TestProcessorAsync(service, cancellationToken));

        if (string.IsNullOrWhiteSpace(apiKeyCredential))
        {
            results.Add(new MvpServiceTestResult(
                "Public PDF upload",
                "SKIP",
                "API key не указан; функциональный тест Public API пропущен."));
            return results;
        }

        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            results.Add(new MvpServiceTestResult(
                "Public PDF upload",
                "SKIP",
                "PDF-файл не выбран или не найден; функциональный тест Public API пропущен."));
            return results;
        }

        results.AddRange(await TestPdfUploadAsync(apiKeyCredential, pdfPath, cancellationToken));
        return results;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<MvpServiceTestResult> TestHealthEndpointAsync(
        string path,
        string step,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        var status = ReadString(document.RootElement, "status") ?? "unknown";
        var checkedAt = ReadString(document.RootElement, "checkedAt");
        var ok = response.IsSuccessStatusCode && string.Equals(status, "healthy", StringComparison.OrdinalIgnoreCase);
        var message = checkedAt is null
            ? $"HTTP {(int)response.StatusCode}, status={status}"
            : $"HTTP {(int)response.StatusCode}, status={status}, checkedAt={checkedAt}";

        return new MvpServiceTestResult(step, ok ? "PASS" : "FAIL", message);
    }

    private async Task<MvpServiceTestResult> TestProcessorAsync(
        MvpServiceDescriptor service,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/api/admin/processors/{Uri.EscapeDataString(service.ProcessorKey)}",
            cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new MvpServiceTestResult(
                "Processor passive status",
                "FAIL",
                $"HTTP {(int)response.StatusCode}: {ReadError(document)}");
        }

        var health = ReadString(document.RootElement, "health") ?? "unknown";
        var queue = document.RootElement.GetProperty("queue");
        var queued = ReadInt(queue, "queued");
        var processing = ReadInt(queue, "processing");
        var failed = ReadInt(queue, "failed");
        var blocked = ReadInt(queue, "blocked");

        return new MvpServiceTestResult(
            "Processor passive status",
            health is "unhealthy" ? "FAIL" : "PASS",
            $"health={health}, queued={queued}, processing={processing}, failed={failed}, blocked={blocked}");
    }

    private async Task<IReadOnlyList<MvpServiceTestResult>> TestPdfUploadAsync(
        string apiKeyCredential,
        string pdfPath,
        CancellationToken cancellationToken)
    {
        var results = new List<MvpServiceTestResult>();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/pdf-stamp-recognition/jobs");
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", apiKeyCredential.Trim());

        await using var fileStream = File.OpenRead(pdfPath);
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent
        {
            { fileContent, "file", Path.GetFileName(pdfPath) }
        };
        request.Content = form;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);

        if (response.StatusCode is HttpStatusCode.OK)
        {
            var hash = ReadString(document.RootElement, "hash") ?? "unknown";
            results.Add(new MvpServiceTestResult(
                "Public PDF upload",
                "PASS",
                $"HTTP 200, cached result returned, hash={hash}."));
            return results;
        }

        if (response.StatusCode is not HttpStatusCode.Accepted)
        {
            results.Add(new MvpServiceTestResult(
                "Public PDF upload",
                "FAIL",
                $"HTTP {(int)response.StatusCode}: {ReadError(document)}"));
            return results;
        }

        var jobId = ReadString(document.RootElement, "jobId");
        var hashAccepted = ReadString(document.RootElement, "hash");
        var initialStatus = ReadString(document.RootElement, "status") ?? "accepted";
        results.Add(new MvpServiceTestResult(
            "Public PDF upload",
            "PASS",
            $"HTTP 202, jobId={jobId}, hash={hashAccepted}, status={initialStatus}."));

        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(hashAccepted))
        {
            results.Add(new MvpServiceTestResult(
                "Public PDF polling",
                "WARN",
                "Ответ upload не содержит jobId/hash; polling пропущен."));
            return results;
        }

        results.Add(await PollPdfResultAsync(apiKeyCredential, hashAccepted, cancellationToken));
        return results;
    }

    private async Task<MvpServiceTestResult> PollPdfResultAsync(
        string apiKeyCredential,
        string hash,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/pdf-stamp-recognition/results/{Uri.EscapeDataString(hash)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", apiKeyCredential.Trim());

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            using var document = await ReadJsonAsync(response, cancellationToken);

            if (response.StatusCode is HttpStatusCode.OK)
            {
                var status = ReadString(document.RootElement, "status") ?? "completed";
                return new MvpServiceTestResult(
                    "Public PDF polling",
                    "PASS",
                    $"Result available after poll {attempt}, status={status}.");
            }

            if (response.StatusCode is HttpStatusCode.Accepted)
            {
                var status = ReadString(document.RootElement, "status") ?? "processing";
                if (attempt < 6)
                {
                    continue;
                }

                return new MvpServiceTestResult(
                    "Public PDF polling",
                    "WARN",
                    $"Job still active after polling window, status={status}. Worker may still be processing or not running.");
            }

            return new MvpServiceTestResult(
                "Public PDF polling",
                "FAIL",
                $"HTTP {(int)response.StatusCode}: {ReadError(document)}");
        }

        return new MvpServiceTestResult(
            "Public PDF polling",
            "WARN",
            "Polling window finished without a terminal response.");
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            content = "{}";
        }

        return JsonDocument.Parse(content);
    }

    private static string ReadError(JsonDocument document)
    {
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            var code = ReadString(error, "code") ?? "error";
            var message = ReadString(error, "message") ?? "No error message.";
            return $"{code}: {message}";
        }

        return "Unexpected response.";
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        return ReadString(element, propertyName)
            ?? throw new InvalidOperationException($"Response property '{propertyName}' is missing.");
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : 0;
    }

}
