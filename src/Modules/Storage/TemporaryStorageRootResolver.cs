namespace CenteralES.Storage;

public static class TemporaryStorageRootResolver
{
    public static string Resolve(string? configuredRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return configuredRoot;
        }

        return Path.Combine(Path.GetTempPath(), "centerales-server", "temporary-files");
    }
}
