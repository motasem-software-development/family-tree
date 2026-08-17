namespace FamilyTree.Import.Tests;

public static class TestPaths
{
    public static string FamilyTreePdf { get; } = Resolve();

    private static string Resolve()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "familytree.pdf")))
            dir = dir.Parent;

        return dir is null
            ? throw new FileNotFoundException("familytree.pdf not found above the test binary.")
            : Path.Combine(dir.FullName, "familytree.pdf");
    }
}
