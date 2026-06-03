using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CenteralES.Admin.Bootstrap.WinForms;

internal sealed class MvpHttpTestClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ResultJsonDisplayOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };
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

    public async Task<IReadOnlyList<MvpServiceTestResult>> TestServiceAvailabilityAsync(
        MvpServiceDescriptor service,
        CancellationToken cancellationToken)
    {
        var results = new List<MvpServiceTestResult>();

        results.Add(await TestHealthEndpointAsync("/health/live", "Web live", cancellationToken));
        results.Add(await TestHealthEndpointAsync("/health/ready", "Web ready", cancellationToken));
        results.Add(await TestProcessorAsync(service, cancellationToken));
        return results;
    }

    public async Task<IReadOnlyList<MvpServiceTestResult>> RunPdfStampRecognitionDemoAsync(
        string apiKeyCredential,
        string pdfPath,
        string hashAlgorithm,
        CancellationToken cancellationToken)
    {
        var results = new List<MvpServiceTestResult>
        {
            await TestHealthEndpointAsync("/health/live", "Web live", cancellationToken),
            await TestHealthEndpointAsync("/health/ready", "Web ready", cancellationToken)
        };

        if (string.IsNullOrWhiteSpace(apiKeyCredential))
        {
            results.Add(new MvpServiceTestResult(
                "Public API credential",
                "FAIL",
                "Укажите готовый ключ в формате keyId.secret."));
            return results;
        }

        if (!IsValidApiKeyCredential(apiKeyCredential))
        {
            results.Add(new MvpServiceTestResult(
                "Public API credential",
                "FAIL",
                "Ключ должен быть вставлен целиком в формате keyId.secret."));
            return results;
        }

        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath.Trim()))
        {
            results.Add(new MvpServiceTestResult(
                "PDF input",
                "FAIL",
                "PDF-файл не выбран или не найден."));
            return results;
        }

        results.AddRange(await TestPdfUploadAsync(
            apiKeyCredential.Trim(),
            pdfPath.Trim(),
            string.IsNullOrWhiteSpace(hashAlgorithm) ? "sha256" : hashAlgorithm.Trim(),
            cancellationToken));
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
        string hashAlgorithm,
        CancellationToken cancellationToken)
    {
        var results = new List<MvpServiceTestResult>();

        var uploadPath = $"/api/pdf-stamp-recognition/jobs?hashAlgorithm={Uri.EscapeDataString(hashAlgorithm)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadPath);
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
            var hash = ReadString(document.RootElement, "hash");
            results.Add(new MvpServiceTestResult(
                "Public PDF upload",
                "PASS",
                $"HTTP 200, cached result returned. {ReadResultSummary(document.RootElement)}"));
            results.Add(new MvpServiceTestResult(
                "Public result JSON",
                "PASS",
                ReadResultJson(document.RootElement)));
            if (!string.IsNullOrWhiteSpace(hash))
            {
                results.Add(await GetPdfResultAsync(apiKeyCredential, hash, cancellationToken));
            }

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
            $"HTTP 202, {ReadJobSummary(document.RootElement)}, status={initialStatus}."));

        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(hashAccepted))
        {
            results.Add(new MvpServiceTestResult(
                "Public PDF polling",
                "WARN",
                "Ответ upload не содержит jobId/hash; polling пропущен."));
            return results;
        }

        results.Add(await GetJobStatusAsync(apiKeyCredential, jobId, "Public job status", cancellationToken));
        results.Add(await PollPdfResultAsync(apiKeyCredential, hashAccepted, cancellationToken));
        results.Add(await GetJobStatusAsync(apiKeyCredential, jobId, "Public final job status", cancellationToken));
        return results;
    }

    private async Task<MvpServiceTestResult> GetJobStatusAsync(
        string apiKeyCredential,
        string jobId,
        string step,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/jobs/{Uri.EscapeDataString(jobId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", apiKeyCredential.Trim());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);

        return response.StatusCode is HttpStatusCode.OK
            ? new MvpServiceTestResult(step, "PASS", ReadJobSummary(document.RootElement))
            : new MvpServiceTestResult(step, "FAIL", $"HTTP {(int)response.StatusCode}: {ReadError(document)}");
    }

    private async Task<MvpServiceTestResult> GetPdfResultAsync(
        string apiKeyCredential,
        string hash,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pdf-stamp-recognition/results/{Uri.EscapeDataString(hash)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", apiKeyCredential.Trim());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);

        return response.StatusCode is HttpStatusCode.OK
            ? new MvpServiceTestResult("Public result by hash", "PASS", $"{ReadResultSummary(document.RootElement)}{Environment.NewLine}{ReadResultJson(document.RootElement)}")
            : new MvpServiceTestResult("Public result by hash", "FAIL", $"HTTP {(int)response.StatusCode}: {ReadError(document)}");
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
                return new MvpServiceTestResult(
                    "Public PDF polling",
                    "PASS",
                    $"Result available after poll {attempt}. {ReadResultSummary(document.RootElement)}{Environment.NewLine}{ReadResultJson(document.RootElement)}");
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

    private static bool IsValidApiKeyCredential(string value)
    {
        var trimmed = value.Trim();
        var separator = trimmed.IndexOf('.', StringComparison.Ordinal);
        return separator > 0 && separator < trimmed.Length - 1;
    }

    private static string ReadJobSummary(JsonElement element)
    {
        var hash = ReadString(element, "hash") ?? "unknown";
        var jobId = ReadString(element, "jobId") ?? "unknown";
        var status = ReadString(element, "status") ?? "unknown";
        var attempt = ReadInt(element, "attemptNumber");
        var deduplicated = ReadBool(element, "deduplicated");

        return $"jobId={jobId}, hash={hash}, status={status}, attempt={attempt}, deduplicated={deduplicated}";
    }

    private static string ReadResultSummary(JsonElement element)
    {
        var hash = ReadString(element, "hash") ?? "unknown";
        var jobId = ReadString(element, "jobId") ?? "unknown";
        var status = ReadString(element, "status") ?? "unknown";
        var contract = ReadString(element, "contractVersion") ?? "unknown";

        return $"jobId={jobId}, hash={hash}, status={status}, contract={contract}";
    }

    private static string ReadResultJson(JsonElement element)
    {
        if (!element.TryGetProperty("result", out var result))
        {
            return "result: <missing>";
        }

        return $"result:{Environment.NewLine}{JsonSerializer.Serialize(result, ResultJsonDisplayOptions)}";
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

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean();
    }

}
