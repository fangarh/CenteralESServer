internal static class AdminUiEndpoints
{
    public static void MapAdminUiEndpoints(this WebApplication app)
    {
        app.MapGet("/favicon.ico", () => Results.NoContent());

        MapAdminAsset(app, "app.css", "text/css; charset=utf-8");
        MapAdminAsset(app, "app.js", "text/javascript; charset=utf-8");
        MapAdminAsset(app, "formatters.js", "text/javascript; charset=utf-8");
        MapAdminAsset(app, "dom.js", "text/javascript; charset=utf-8");
        MapAdminAsset(app, "http.js", "text/javascript; charset=utf-8");
        MapAdminAsset(app, "confirm-dialog.js", "text/javascript; charset=utf-8");

        app.MapGet("/admin/{**path}", (IWebHostEnvironment environment) => Results.File(
            Path.Combine(environment.WebRootPath, "admin", "index.html"),
            "text/html; charset=utf-8"));
    }

    private static void MapAdminAsset(WebApplication app, string fileName, string contentType)
    {
        app.MapGet($"/admin/{fileName}", (IWebHostEnvironment environment) => Results.File(
            Path.Combine(environment.WebRootPath, "admin", fileName),
            contentType));
    }
}
