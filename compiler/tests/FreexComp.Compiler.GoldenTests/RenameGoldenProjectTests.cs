using System.Text;
using FreexComp.Compiler.Text;
using FreexComp.LanguageService;

namespace FreexComp.Compiler.GoldenTests;

/// <summary>
/// CMP-1511 (C-453, card F13-001 acceptance 1–3): the rename corpus <c>rename/</c> next to this file. Each case folder holds
/// <c>before/</c> (the project), <c>after/</c> (the expected files after the rename), <c>rename.txt</c> (<c>file:</c> the file
/// the rename starts in, <c>at:</c> the first text at the position, <c>name:</c> the new name) and <c>expected.out</c> (what
/// the program prints, before and after). The main file is <c>P.pas</c>. The rename runs over a copy on disk (the real file
/// system, the RTL of the golden harness); its edits and file rename are applied as the IDE applies them.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class RenameGoldenProjectTests
{
    private const string Main = "P.pas";

    private static string CorpusDirectory => Path.Combine(GoldenSetup.RepositoryRoot, "compiler", "tests", "FreexComp.Compiler.GoldenTests", "rename");

    public static TheoryData<string> Cases() =>
        [.. Directory.GetDirectories(CorpusDirectory).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Req", "CMP-1511")]
    public async Task RenamedProjectsCompileWithUnchangedOutput(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        var source = Path.Combine(CorpusDirectory, name);
        var spec = File.ReadAllLines(Path.Combine(source, "rename.txt")).Select(l => l.Split(": ", 2)).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        var original = Copy(Path.Combine(source, "before"));
        var renamed = Copy(Path.Combine(source, "before"));
        try
        {
            var result = Rename(renamed, spec["file"], spec["at"], spec["name"], ct);

            Assert.True(result.Success, result.Error);
            Apply(renamed, result);
            AssertSameFiles(Path.Combine(source, "after"), renamed);
            foreach (var copy in new[] { original, renamed })
            {
                File.Copy(Path.Combine(source, "expected.out"), Path.Combine(copy, Path.ChangeExtension(Main, ".out")));
                var failure = await GoldenRunner.RunAsync(new GoldenCase($"rename/{name}/{(copy == original ? "before" : "after")}", Path.Combine(copy, Main)), false, GoldenSetup.Default, ct);
                Assert.True(failure is null, failure);
            }
        }
        finally
        {
            GoldenRunner.TryDelete(original);
            GoldenRunner.TryDelete(renamed);
        }
    }

    /// <summary>CMP-1511 (acceptance 3): renaming a local to the name of another local of the same routine returns no edit; the copy stays byte-identical.</summary>
    [Fact]
    [Trait("Req", "CMP-1511")]
    public void ConflictLeavesTheProjectUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var before = Path.Combine(CorpusDirectory, "Local", "before");
        var copy = Copy(before);
        try
        {
            var result = Rename(copy, Main, "Count, Step", "Step", ct);

            Assert.False(result.Success);
            Assert.NotEmpty(result.Conflicts);
            Assert.Empty(result.Edits);
            Assert.Null(result.FileRename);
            Apply(copy, result);
            AssertSameFiles(before, copy);
        }
        finally
        {
            GoldenRunner.TryDelete(copy);
        }
    }

    private static string Copy(string from)
    {
        var to = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        return to;
    }

    private static RenameResult Rename(string directory, string file, string at, string newName, CancellationToken ct)
    {
        var workspace = new Workspace(PhysicalFileSystem.Instance);
        workspace.SetProject(new CompilationOptions
        {
            MainFile = Path.Combine(directory, Main), RtlDirectory = GoldenSetup.Default.RtlDirectory, ObjectDirectory = Path.Combine(directory, "obj"),
        });
        var path = Path.Combine(directory, file);
        var position = File.ReadAllText(path, Encoding.UTF8).IndexOf(at, StringComparison.Ordinal);
        Assert.True(position >= 0, at);
        return RenameService.Rename(workspace.GetDocument(path), position, newName, ct);
    }

    /// <summary>The edits (each against the file's text), then the file rename — as the IDE applies a rename.</summary>
    private static void Apply(string directory, RenameResult result)
    {
        foreach (var edit in result.Edits)
        {
            Assert.True(PathUtil.AreEqual(Path.GetDirectoryName(edit.Path)!, directory), edit.Path);
            var text = new StringBuilder(File.ReadAllText(edit.Path, Encoding.UTF8));
            foreach (var change in edit.Changes.OrderByDescending(c => c.Span.Start))
            {
                text.Remove(change.Span.Start, change.Span.Length).Insert(change.Span.Start, change.NewText);
            }

            File.WriteAllText(edit.Path, text.ToString(), new UTF8Encoding(false));
        }

        if (result.FileRename is { } rename)
        {
            File.Move(rename.OldPath, rename.NewPath);
        }
    }

    /// <summary>The same file names, each with the same bytes.</summary>
    private static void AssertSameFiles(string expected, string actual)
    {
        static string[] Names(string directory) => [.. Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase)!];

        Assert.Equal(Names(expected), Names(actual));
        foreach (var name in Names(expected))
        {
            Assert.True(File.ReadAllBytes(Path.Combine(expected, name)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(actual, name))),
                $"{name} differs:\n{File.ReadAllText(Path.Combine(actual, name))}");
        }
    }
}
