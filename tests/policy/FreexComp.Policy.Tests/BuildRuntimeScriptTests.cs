using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FreexComp.Policy.Tests;

/// <summary>
/// LSRC-010…012: <c>scripts/build-runtime.ps1</c> compiles every library unit from the shipped source, and the user help
/// page describes it. The script runs with the compiler built beside this assembly.
/// </summary>
[Trait("Phase", "8")]
public sealed partial class BuildRuntimeScriptTests
{
    private const string ScriptName = "scripts/build-runtime.ps1";

    [GeneratedRegex(@"^build-runtime: (\d+) units, (\d+) errors, (\d+) warnings$")]
    private static partial Regex SummaryLine();

    private static string Script => RepoPaths.Combine("scripts", "build-runtime.ps1");

    private static string ExcludeFile => RepoPaths.Combine("scripts", "build-runtime.exclude.txt");

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "LSRC-010")]
    [Trait("Req", "LSRC-011")]
    public async Task CompilesAllLibraryUnits()
    {
        var layout = new LibrarySourceLayout(RepoPaths.Root);
        var objects = NewDirectory();
        try
        {
            var before = Listing(layout);
            var (exit, output) = await RunScriptAsync("-Home", RepoPaths.Root, "-ObjDir", objects);
            var after = Listing(layout);

            Assert.True(exit == 0, $"exit code {exit}\n{output}");
            var (units, errors, _) = Summary(output);
            Assert.Equal(0, errors);

            var excluded = Exclusions();
            var expected = layout.UnitFiles().Select(Path.GetFileNameWithoutExtension).Where(name => !excluded.Contains(name!)).ToList();
            Assert.Equal(expected.Count, units);

            // the generated program names exactly these units, in ordinal order
            var program = await File.ReadAllTextAsync(Path.Combine(objects, "RuntimeAll.pas"), TestContext.Current.CancellationToken);
            var named = program[(program.IndexOf("uses", StringComparison.Ordinal) + 4)..program.IndexOf(';', program.IndexOf("uses", StringComparison.Ordinal))]
                .Split(',').Select(n => n.Trim()).ToList();
            Assert.Equal(expected.Order(StringComparer.Ordinal), named);
            Assert.EndsWith("begin\nend.\n", program.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);

            // every unit was compiled into the object directory, nothing was written under the source directories
            var objectFiles = Directory.GetFiles(objects, "*.obj").Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(expected, name => Assert.True(objectFiles.Contains(name!), $"no object file for unit {name}\n{output}"));
            Assert.Equal(before, after);
        }
        finally
        {
            DeleteDirectory(objects);
        }
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "LSRC-011")]
    public async Task FailsOnBrokenUnit()
    {
        var home = NewDirectory();
        try
        {
            // a copy of the source directories in which one unit no longer parses
            var source = new LibrarySourceLayout(RepoPaths.Root);
            var copy = new LibrarySourceLayout(home);
            foreach (var file in source.AllFiles())
            {
                var target = Path.Combine(home, Path.GetRelativePath(source.Home, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }

            var broken = Path.Combine(copy.RtlRoot, "StrUtils.pas");
            Assert.True(File.Exists(broken), "the unit to break is missing");
            await File.WriteAllTextAsync(
                broken, "unit StrUtils;\ninterface\n  this is not a declaration\nimplementation\nend.\n", new UTF8Encoding(false), TestContext.Current.CancellationToken);

            var (exit, output) = await RunScriptAsync("-Home", home);

            Assert.True(exit == 1, $"exit code {exit}\n{output}");
            Assert.Contains(broken + "(3,", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("): error ", output, StringComparison.Ordinal);
            var (units, errors, _) = Summary(output);
            Assert.Equal(copy.UnitFiles().Count - Exclusions().Count, units);
            Assert.True(errors > 0, output);
        }
        finally
        {
            DeleteDirectory(home);
        }
    }

    [Fact]
    [Trait("Req", "LSRC-012")]
    public void HelpPageExists()
    {
        var page = RepoPaths.Combine("help", "library-source.md");
        Assert.True(File.Exists(page), "help/library-source.md is missing");
        var text = File.ReadAllText(page);
        Assert.Contains(ScriptName, text, StringComparison.Ordinal);
        Assert.True(File.Exists(Script), ScriptName + " is missing");
        Assert.True(File.Exists(ExcludeFile), "scripts/build-runtime.exclude.txt is missing");

        // the page names the source directories and uses no internal ids
        foreach (var root in LibrarySourceLayout.RootNames)
        {
            Assert.Contains(root, text, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(@"\b[A-Z]{2,5}(-[A-Z0-9]{1,4})?-\d{3}\b", text);
    }

    /// <summary>Every line of the exclusion list names a unit and, after <c>#</c>, the reason.</summary>
    [Fact]
    [Trait("Req", "LSRC-011")]
    public void ExclusionListIsWellFormed()
    {
        Assert.True(File.Exists(ExcludeFile), "scripts/build-runtime.exclude.txt is missing");
        var units = new LibrarySourceLayout(RepoPaths.Root).UnitFiles().Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(ExcludeFile).Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#'))
        {
            var parts = line.Split('#', 2);
            Assert.True(parts.Length == 2 && parts[1].Trim().Length > 0, $"'{line}': no reason after '#'");
            Assert.True(units.Contains(parts[0].Trim()), $"'{line}': not a library unit");
        }

        Assert.Equal(Exclusions().Count, File.ReadAllLines(ExcludeFile).Count(l => l.Trim().Length > 0 && l.Trim()[0] != '#'));
    }

    private static HashSet<string> Exclusions() =>
        File.ReadAllLines(ExcludeFile).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static (int Units, int Errors, int Warnings) Summary(string output)
    {
        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(l => l.StartsWith("build-runtime:", StringComparison.Ordinal)).ToList();
        Assert.True(lines.Count == 1, "expected one summary line\n" + output);
        var match = SummaryLine().Match(lines[0]);
        Assert.True(match.Success, "summary line: " + lines[0]);
        return (Number(1), Number(2), Number(3));

        int Number(int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Runs the script with the compiler beside this assembly; the variable of the default home is not passed on.</summary>
    private static async Task<(int ExitCode, string Output)> RunScriptAsync(params string[] arguments)
    {
        Assert.True(File.Exists(Script), ScriptName + " is missing");
        var opc = Path.Combine(AppContext.BaseDirectory, "opc.exe");
        Assert.True(File.Exists(opc), opc + " is missing");
        Assert.True(File.Exists(RepoPaths.Combine("third_party", "llvm", "bin", "clang.exe")), "Pinned toolchain missing (run scripts/bootstrap.ps1).");
        string[] all =
        [
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Script,
            .. arguments,
            "-Opc", opc,
        ];
        return await PlatformPrograms.RunAsync(
            Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            all,
            TimeSpan.FromMinutes(20),
            TestContext.Current.CancellationToken);
    }

    /// <summary>Every directory and file under the source directories with size and time of the last write.</summary>
    private static List<string> Listing(LibrarySourceLayout layout)
    {
        var entries = new List<string>();
        foreach (var root in layout.Roots)
        {
            entries.AddRange(Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Select(d => layout.Display(d) + "/"));
            entries.AddRange(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f =>
            {
                var info = new FileInfo(f);
                return string.Create(CultureInfo.InvariantCulture, $"{layout.Display(f)} {info.Length} {info.LastWriteTimeUtc.Ticks}");
            }));
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "freexcomp-runtime-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
