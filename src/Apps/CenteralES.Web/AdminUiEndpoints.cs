internal static class AdminUiEndpoints
{
    public static void MapAdminUiEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/{**path}", (IWebHostEnvironment environment) => Results.File(
            Path.Combine(environment.WebRootPath, "admin", "index.html"),
            "text/html; charset=utf-8"));
    }
}
