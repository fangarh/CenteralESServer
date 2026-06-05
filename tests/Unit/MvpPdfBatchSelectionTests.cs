using CenteralES.Admin.Bootstrap.WinForms;

namespace CenteralES.UnitTests;

public sealed class MvpPdfBatchSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"centerales-batch-{Guid.NewGuid():N}");

    public MvpPdfBatchSelectionTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ListPdfFiles_returns_top_level_pdf_files_ordered_by_name()
    {
        File.WriteAllText(Path.Combine(_root, "b.pdf"), "b");
        File.WriteAllText(Path.Combine(_root, "a.PDF"), "a");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "notes");
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        File.WriteAllText(Path.Combine(_root, "nested", "c.pdf"), "c");

        var files = MvpPdfBatchSelection.ListPdfFiles(_root);

        Assert.Equal(
            new[]
            {
                Path.Combine(_root, "a.PDF"),
                Path.Combine(_root, "b.pdf")
            },
            files);
    }

    [Fact]
    public void ListPdfFiles_rejects_missing_folder()
    {
        var missing = Path.Combine(_root, "missing");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MvpPdfBatchSelection.ListPdfFiles(missing));

        Assert.Contains("Folder was not found", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
