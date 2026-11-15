namespace FreexComp.Compiler.Tests;

/// <summary>
/// C-149: the RTL the compiler's own tests compile against is the real one in <c>rtl/src</c> (the mini RTL of CMP-630 is
/// gone). In-memory compilations get its <c>System.pas</c> and the include files it reads (<c>*.inc</c>), never its other units.
/// </summary>
internal static class TestRtl
{
    /// <summary><c>rtl/src</c>.</summary>
    public static string Directory { get; } = Path.Combine(RepoPaths.Root, "rtl", "src");

    /// <summary><c>rtl/winapi/def</c>: the definition files of the DLLs <c>System</c> imports from (CMP-620).</summary>
    public static string DefinitionDirectory { get; } = Path.Combine(RepoPaths.Root, "rtl", "winapi", "def");

    private static readonly Lazy<(string Name, string Text)[]> SystemFiles = new(() =>
        [.. System.IO.Directory.GetFiles(Directory, "*.inc").Order(StringComparer.Ordinal).Prepend(Path.Combine(Directory, "System.pas"))
            .Select(p => (Path.GetFileName(p), File.ReadAllText(p)))]);

    /// <summary>The text of <c>rtl/src/System.pas</c>.</summary>
    public static string SystemSource => SystemFiles.Value[0].Text;

    /// <summary>Adds <c>System.pas</c> and the RTL's <c>*.inc</c> files to <paramref name="fs"/> under <paramref name="directory"/>.</summary>
    public static InMemoryFileSystem AddSystem(this InMemoryFileSystem fs, string directory = @"C:\rtl")
    {
        foreach (var (name, text) in SystemFiles.Value)
        {
            fs.Add(Path.Combine(directory, name), text);
        }

        return fs;
    }
}
