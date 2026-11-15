using FreexComp.Compiler;

namespace FreexComp.LanguageService.Tests;

/// <summary>In-memory projects against the real RTL's <c>System</c> (as the compiler's tests do, C-149).</summary>
internal static class TestFiles
{
    public const string Dir = @"C:\p";

    public const string RtlDir = @"C:\rtl";

    private static readonly Lazy<(string Name, string Text)[]> SystemFiles = new(() =>
    {
        var rtl = Path.Combine(RepoPaths.Root, "rtl", "src");
        return [.. Directory.GetFiles(rtl, "*.inc").Order(StringComparer.Ordinal).Prepend(Path.Combine(rtl, "System.pas"))
            .Select(p => (Path.GetFileName(p), File.ReadAllText(p)))];
    });

    /// <summary>A file system with <c>System</c> under <see cref="RtlDir"/> and the given files under <see cref="Dir"/>.</summary>
    public static InMemoryFileSystem Files(params (string Name, string Text)[] files)
    {
        var fs = new InMemoryFileSystem();
        foreach (var (name, text) in SystemFiles.Value)
        {
            fs.Add(Path.Combine(RtlDir, name), text);
        }

        foreach (var (name, text) in files)
        {
            fs.Add(Path.Combine(Dir, name), text);
        }

        return fs;
    }

    public static CompilationOptions Options(string main = "P.pas") =>
        new() { MainFile = Path.Combine(Dir, main), RtlDirectory = RtlDir, ObjectDirectory = Path.Combine(Dir, "obj") };

    public static string PathOf(string name) => Path.Combine(Dir, name);
}
