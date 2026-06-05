namespace CenteralES.Admin.Bootstrap.WinForms;

public static class MvpPdfBatchSelection
{
    public static IReadOnlyList<string> ListPdfFiles(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath.Trim()))
        {
            throw new InvalidOperationException($"Folder was not found: {folderPath}");
        }

        return Directory
            .EnumerateFiles(folderPath.Trim(), "*.pdf", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
