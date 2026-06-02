using CenteralES.AccessControl;
using CenteralES.Admin;
using CenteralES.PdfStampRecognition;

internal static class AdminJobEndpoints
{
    public static void MapAdminJobEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/jobs", async (
            string? capability,
            string? status,
            string? hash,
            int? limit,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminJobReadStore readStore,
            CancellationToken cancellationToken) =>
        {
            var authorization = await ApiAuthorization.AuthorizeAdminApiAsync(
                request,
                adminAuthenticator,
                requireCsrf: false,
                cancellationToken);
            if (authorization.Error is not null)
            {
                return authorization.Error;
            }

            if (!ApiMappings.TryParseOptionalStatus(status, out var parsedStatus))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Job status '{status}' is not valid."));
            }

            var jobs = await readStore.ListJobsAsync(
                new AdminProcessingJobListQuery(
                    string.IsNullOrWhiteSpace(capability) ? null : capability,
                    parsedStatus,
                    string.IsNullOrWhiteSpace(hash) ? null : hash,
                    limit ?? 50),
                cancellationToken);

            return Results.Ok(new AdminJobListResponse(jobs.Select(ApiMappings.ToAdminJobListItemResponse).ToArray()));
        })
            .WithName("AdminListJobs");

        app.MapGet("/api/admin/jobs/{jobId}", async (
            string jobId,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminJobReadStore readStore,
            CancellationToken cancellationToken) =>
        {
            var authorization = await ApiAuthorization.AuthorizeAdminApiAsync(
                request,
                adminAuthenticator,
                requireCsrf: false,
                cancellationToken);
            if (authorization.Error is not null)
            {
                return authorization.Error;
            }

            if (!Guid.TryParse(jobId, out var parsedJobId))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Job id '{jobId}' is not a valid GUID."));
            }

            var job = await readStore.GetJobAsync(parsedJobId, cancellationToken);
            return job is null
                ? Results.NotFound(ApiErrorResponse.Create("job_not_found", $"Job '{jobId}' was not found."))
                : Results.Ok(ApiMappings.ToAdminJobDetailsResponse(job));
        })
            .WithName("AdminGetJob");

        app.MapGet("/api/admin/jobs/{jobId}/support-report", async (
            string jobId,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminJobReadStore readStore,
            CancellationToken cancellationToken) =>
        {
            var authorization = await ApiAuthorization.AuthorizeAdminApiAsync(
                request,
                adminAuthenticator,
                requireCsrf: false,
                cancellationToken);
            if (authorization.Error is not null)
            {
                return authorization.Error;
            }

            if (!Guid.TryParse(jobId, out var parsedJobId))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Job id '{jobId}' is not a valid GUID."));
            }

            var report = await readStore.GetJobSupportReportAsync(
                parsedJobId,
                PdfStampRecognitionConstants.ProcessorKey,
                cancellationToken);
            return report is null
                ? Results.NotFound(ApiErrorResponse.Create("job_not_found", $"Job '{jobId}' was not found."))
                : Results.Ok(ApiMappings.ToAdminJobSupportReportResponse(report));
        })
            .WithName("AdminGetJobSupportReport");

        app.MapPost("/api/admin/jobs/{jobId}/retry", async (
            string jobId,
            AdminManualRetryRequestBody retry,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminProcessingActionStore actionStore,
            CancellationToken cancellationToken) =>
        {
            var authorization = await ApiAuthorization.AuthorizeAdminApiAsync(
                request,
                adminAuthenticator,
                requireCsrf: true,
                cancellationToken);
            if (authorization.Error is not null)
            {
                return authorization.Error;
            }

            if (!Guid.TryParse(jobId, out var parsedJobId))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Job id '{jobId}' is not a valid GUID."));
            }

            var comment = retry.Comment?.Trim();
            if (comment?.Length > 1000)
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Retry comment must not exceed 1000 characters."));
            }

            var principal = authorization.Principal!;
            var result = await actionStore.ManualRetryJobAsync(
                new AdminManualRetryJobCommand(
                    parsedJobId,
                    principal.UserId,
                    principal.Login,
                    DateTimeOffset.UtcNow,
                    comment,
                    request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    request.Headers.UserAgent.ToString()),
                cancellationToken);

            return result switch
            {
                AdminManualRetryJobSuccess success => Results.Accepted(
                    $"/api/jobs/{success.NewJobId:N}",
                    new AdminManualRetryResponse(
                        success.SourceJobId.ToString("N"),
                        success.NewJobId.ToString("N"),
                        success.ContentHash,
                        success.AttemptNumber,
                        ApiMappings.ToPublicStatus(success.NewStatus),
                        success.AuditId.ToString("N"))),
                AdminManualRetryJobNotFound => Results.NotFound(
                    ApiErrorResponse.Create("job_not_found", $"Job '{jobId}' was not found.")),
                AdminManualRetryJobConflict => Results.Json(
                    ApiErrorResponse.Create("retry_not_allowed", "Manual retry is allowed only for the current failed or blocked job."),
                    statusCode: StatusCodes.Status409Conflict),
                _ => throw new InvalidOperationException($"Unknown manual retry result '{result.GetType().Name}'.")
            };
        })
            .WithName("AdminManualRetryJob");
    }
}
