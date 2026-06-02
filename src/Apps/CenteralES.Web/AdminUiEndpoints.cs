internal static class AdminUiEndpoints
{
    public static void MapAdminUiEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/app.css", (IWebHostEnvironment environment) => Results.File(
            Path.Combine(environment.WebRootPath, "admin", "app.css"),
            "text/css; charset=utf-8"));

        app.MapGet("/admin/app.js", (IWebHostEnvironment environment) => Results.File(
            Path.Combine(environment.WebRootPath, "admin", "app.js"),
            "text/javascript; charset=utf-8"));

        app.MapGet("/admin/{**path}", (IWebHostEnvironment environment) => Results.File(
            Path.Combine(environment.WebRootPath, "admin", "index.html"),
            "text/html; charset=utf-8"));
    }
}
