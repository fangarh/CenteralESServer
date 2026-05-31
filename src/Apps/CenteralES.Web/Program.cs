using CenteralES.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health/live", () =>
    Results.Ok(new HealthResponse("healthy", DateTimeOffset.UtcNow)))
    .WithName("LiveHealth");

app.MapGet("/health/ready", () =>
    Results.Ok(new HealthResponse("healthy", DateTimeOffset.UtcNow)))
    .WithName("ReadyHealth");

app.MapPost("/api/pdf-stamp-recognition/jobs", async (HttpRequest request, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "PDF file must be sent as multipart form data."));
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "PDF file is required."));
    }

    await using var stream = file.OpenReadStream();
    var hash = await ContentHash.ComputeSha256Async(stream, cancellationToken);
    var jobId = Guid.NewGuid().ToString("N");

    return Results.Accepted(
        $"/api/jobs/{jobId}",
        new PdfJobResponse(hash, jobId, 1, "queued", Deduplicated: false));
})
.DisableAntiforgery()
.WithName("CreatePdfStampRecognitionJob");

app.MapGet("/api/pdf-stamp-recognition/results/{hash}", (string hash) =>
    Results.NotFound(ApiErrorResponse.Create("result_not_found", $"No result or active processing found for hash '{hash}'.")))
    .WithName("GetPdfStampRecognitionResult");

app.MapGet("/api/jobs/{jobId}", (string jobId) =>
    Results.NotFound(ApiErrorResponse.Create("job_not_found", $"Job '{jobId}' was not found.")))
    .WithName("GetJob");

app.Run();

internal sealed record HealthResponse(string Status, DateTimeOffset CheckedAt);

internal sealed record PdfJobResponse(
    string Hash,
    string JobId,
    int AttemptNumber,
    string Status,
    bool Deduplicated);

internal sealed record ApiErrorResponse(ApiError Error)
{
    public static ApiErrorResponse Create(string code, string message)
    {
        return new ApiErrorResponse(new ApiError(code, message, null, Guid.NewGuid().ToString("N")));
    }
}

internal sealed record ApiError(string Code, string Message, object? Details, string CorrelationId);

public partial class Program;
