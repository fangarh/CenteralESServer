using CenteralES.AccessControl;
using CenteralES.PdfStampRecognition;
using CenteralES.Processing.Queue;
using CenteralES.Storage;

internal static class PublicPdfEndpoints
{
    public static void MapPublicPdfEndpoints(this WebApplication app, long pdfMaxUploadBytes)
    {
        const long multipartOverheadAllowanceBytes = 1L * 1024 * 1024;
        var requestSizeLimitBytes = pdfMaxUploadBytes + multipartOverheadAllowanceBytes;

        app.MapPost("/api/pdf-stamp-recognition/jobs", async (
            HttpRequest request,
            IApiKeyAuthenticator apiKeyAuthenticator,
            IProcessingJobQueue queue,
            IPdfStampRecognitionResultStore resultStore,
            ITemporaryFileStore temporaryFileStore,
            ITemporaryStorageMonitor temporaryStorageMonitor,
            CancellationToken cancellationToken) =>
        {
            var authorization = await ApiAuthorization.AuthorizePublicApiAsync(
                request,
                apiKeyAuthenticator,
                PdfStampRecognitionConstants.Capability,
                cancellationToken);
            if (authorization is not null)
            {
                return authorization;
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "PDF file must be sent as multipart form data."));
            }

            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync(cancellationToken);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return Results.Json(
                    ApiErrorResponse.Create("payload_too_large", $"PDF upload request exceeds the configured limit of {pdfMaxUploadBytes} bytes."),
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }
            catch (InvalidDataException)
            {
                return Results.Json(
                    ApiErrorResponse.Create("payload_too_large", $"PDF upload request exceeds the configured limit of {pdfMaxUploadBytes} bytes."),
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "PDF file is required."));
            }

            if (file.Length > pdfMaxUploadBytes)
            {
                return Results.Json(
                    ApiErrorResponse.Create("payload_too_large", $"PDF file exceeds the configured limit of {pdfMaxUploadBytes} bytes."),
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            await using var stream = file.OpenReadStream();
            var hash = await ContentHash.ComputeSha256Async(stream, cancellationToken);

            var existingResult = await resultStore.GetByHashAsync(hash, cancellationToken);
            if (existingResult is not null)
            {
                return Results.Ok(ApiMappings.ToPdfResultResponse(existingResult));
            }

            var currentJob = await queue.GetCurrentByHashAsync(PdfStampRecognitionConstants.Capability, hash, cancellationToken);
            if (currentJob is not null && ApiMappings.IsPublicPending(currentJob.Status))
            {
                return Results.Accepted(
                    $"/api/jobs/{currentJob.JobId:N}",
                    ApiMappings.ToPdfJobResponse(currentJob, deduplicated: true));
            }

            var temporaryFileKey = $"incoming/{hash.Replace("sha256:", string.Empty, StringComparison.Ordinal)}.pdf";
            var capacity = await temporaryStorageMonitor.CheckCapacityAsync(
                new TemporaryStorageCapacityRequest(file.Length),
                cancellationToken);
            if (capacity.Status is TemporaryStorageCapacityStatus.Full)
            {
                return Results.Json(
                    ApiErrorResponse.Create("temporary_storage_full", "Temporary storage is full. Try again after cleanup frees space."),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            await using (var saveStream = file.OpenReadStream())
            {
                await temporaryFileStore.SaveAsync(temporaryFileKey, saveStream, cancellationToken);
            }

            var enqueueResult = await queue.EnqueueAsync(
                new CreateProcessingJobCommand(PdfStampRecognitionConstants.Capability, hash, temporaryFileKey, DateTimeOffset.UtcNow),
                cancellationToken);

            return Results.Accepted(
                $"/api/jobs/{enqueueResult.JobId:N}",
                new PdfJobResponse(
                    hash,
                    enqueueResult.JobId.ToString("N"),
                    enqueueResult.AttemptNumber,
                    ApiMappings.ToPublicStatus(enqueueResult.Status),
                    enqueueResult.Deduplicated));
        })
        .DisableAntiforgery()
        .WithMetadata(new PdfUploadRequestSizeLimitMetadata(requestSizeLimitBytes))
        .WithName("CreatePdfStampRecognitionJob");

        app.MapGet("/api/pdf-stamp-recognition/results/{hash}", async (
            string hash,
            HttpRequest request,
            IApiKeyAuthenticator apiKeyAuthenticator,
            IProcessingJobQueue queue,
            IPdfStampRecognitionResultStore resultStore,
            CancellationToken cancellationToken) =>
        {
            var authorization = await ApiAuthorization.AuthorizePublicApiAsync(
                request,
                apiKeyAuthenticator,
                PdfStampRecognitionConstants.Capability,
                cancellationToken);
            if (authorization is not null)
            {
                return authorization;
            }

            var result = await resultStore.GetByHashAsync(hash, cancellationToken);
            if (result is not null)
            {
                return Results.Ok(ApiMappings.ToPdfResultResponse(result));
            }

            var currentJob = await queue.GetCurrentByHashAsync(PdfStampRecognitionConstants.Capability, hash, cancellationToken);
            return currentJob is not null && ApiMappings.IsPublicPending(currentJob.Status)
                ? Results.Accepted($"/api/jobs/{currentJob.JobId:N}", ApiMappings.ToPdfJobResponse(currentJob, deduplicated: true))
                : Results.NotFound(ApiErrorResponse.Create("result_not_found", $"No result or active processing found for hash '{hash}'."));
        })
            .WithName("GetPdfStampRecognitionResult");

        app.MapGet("/api/jobs/{jobId}", async (
            string jobId,
            HttpRequest request,
            IApiKeyAuthenticator apiKeyAuthenticator,
            IProcessingJobQueue queue,
            CancellationToken cancellationToken) =>
        {
            var authorization = await ApiAuthorization.AuthorizePublicApiAsync(
                request,
                apiKeyAuthenticator,
                PdfStampRecognitionConstants.Capability,
                cancellationToken);
            if (authorization is not null)
            {
                return authorization;
            }

            if (!Guid.TryParse(jobId, out var parsedJobId))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Job id '{jobId}' is not a valid GUID."));
            }

            var job = await queue.GetJobAsync(parsedJobId, cancellationToken);
            return job is null
                ? Results.NotFound(ApiErrorResponse.Create("job_not_found", $"Job '{jobId}' was not found."))
                : Results.Ok(ApiMappings.ToPdfJobResponse(job, deduplicated: false));
        })
            .WithName("GetJob");
    }

    private sealed class PdfUploadRequestSizeLimitMetadata : Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata
    {
        public PdfUploadRequestSizeLimitMetadata(long maxRequestBodySize)
        {
            MaxRequestBodySize = maxRequestBodySize;
        }

        public long? MaxRequestBodySize { get; }
    }
}
