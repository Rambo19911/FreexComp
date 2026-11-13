namespace FreexComp.E2E.Tests;

/// <summary>Locates the repository root (the folder that contains FreexComp.sln).</summary>
internal static class RepoPaths
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FreexComp.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("FreexComp.sln not found above " + AppContext.BaseDirectory);
    }
}
