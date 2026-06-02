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
            SubmitPdfStampRecognitionJobHandler submitJob,
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
            var requestedHashAlgorithm = ResolveHashAlgorithmParameter(request, form);
            var hashAlgorithm = ContentHashAlgorithm.Sha256;
            if (requestedHashAlgorithm is not null
                && !ContentHashAlgorithms.TryParse(requestedHashAlgorithm, out hashAlgorithm))
            {
                return Results.BadRequest(ApiErrorResponse.Create(
                    "invalid_input",
                    $"Hash algorithm '{requestedHashAlgorithm}' is not supported. Supported values: {ContentHashAlgorithms.Sha256}, {ContentHashAlgorithms.GostR34112012_256}."));
            }

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

            var submitResult = await submitJob.HandleAsync(
                new SubmitPdfStampRecognitionJobCommand(
                    file.OpenReadStream,
                    file.Length,
                    hashAlgorithm,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            return submitResult switch
            {
                SubmitPdfStampRecognitionJobCompleted completed => Results.Ok(ApiMappings.ToPdfResultResponse(completed.Result)),
                SubmitPdfStampRecognitionJobAccepted accepted => Results.Accepted(
                    $"/api/jobs/{accepted.JobId:N}",
                    new PdfJobResponse(
                        accepted.ContentHash,
                        accepted.JobId.ToString("N"),
                        accepted.AttemptNumber,
                        ApiMappings.ToPublicStatus(accepted.Status),
                        accepted.Deduplicated)),
                SubmitPdfStampRecognitionJobTemporaryStorageFull => Results.Json(
                    ApiErrorResponse.Create("temporary_storage_full", "Temporary storage is full. Try again after cleanup frees space."),
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => throw new InvalidOperationException($"Unknown PDF stamp recognition submission result '{submitResult.GetType().Name}'.")
            };
        })
        .DisableAntiforgery()
        .WithMetadata(new PdfUploadRequestSizeLimitMetadata(requestSizeLimitBytes))
        .WithName("CreatePdfStampRecognitionJob");

        app.MapGet("/api/pdf-stamp-recognition/results/{hash}", async (
            string hash,
            HttpRequest request,
            IApiKeyAuthenticator apiKeyAuthenticator,
            IProcessingJobReadStore jobReadStore,
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

            var currentJob = await jobReadStore.GetCurrentByHashAsync(PdfStampRecognitionConstants.Capability, hash, cancellationToken);
            return currentJob is not null && ApiMappings.IsPublicPending(currentJob.Status)
                ? Results.Accepted($"/api/jobs/{currentJob.JobId:N}", ApiMappings.ToPdfJobResponse(currentJob, deduplicated: true))
                : Results.NotFound(ApiErrorResponse.Create("result_not_found", $"No result or active processing found for hash '{hash}'."));
        })
            .WithName("GetPdfStampRecognitionResult");

        app.MapGet("/api/jobs/{jobId}", async (
            string jobId,
            HttpRequest request,
            IApiKeyAuthenticator apiKeyAuthenticator,
            IProcessingJobReadStore jobReadStore,
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

            var job = await jobReadStore.GetJobAsync(parsedJobId, cancellationToken);
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

    private static string? ResolveHashAlgorithmParameter(HttpRequest request, IFormCollection form)
    {
        var queryValue = request.Query["hashAlgorithm"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queryValue))
        {
            return queryValue;
        }

        return form.TryGetValue("hashAlgorithm", out var formValue)
            ? formValue.FirstOrDefault()
            : null;
    }
}
