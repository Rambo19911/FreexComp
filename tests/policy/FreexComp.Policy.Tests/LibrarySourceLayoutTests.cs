using System.Collections.Immutable;
using System.Text.Json;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;

namespace FreexComp.Policy.Tests;

/// <summary>
/// LSRC-001…006: the library source a product tree ships is the source the compiler compiles — complete, readable and
/// without a binary form. The checks read the tree named by <see cref="LibrarySourceLayout.Current"/>: the repository, or
/// an installed directory when <c>FREEXCOMP_LAYOUT_HOME</c> is set. Nothing is compiled.
/// </summary>
[Trait("Phase", "8")]
public sealed class LibrarySourceLayoutTests
{
    /// <summary>Extensions of compiled or linked forms; none may lie under a source directory.</summary>
    private static readonly string[] BinaryExtensions = [".pcu", ".obj", ".lib", ".dll", ".exe", ".o"];

    private static readonly LibrarySourceLayout Layout = LibrarySourceLayout.Current();

    [Fact]
    [Trait("Req", "LSRC-001")]
    [Trait("Req", "LSRC-005")]
    [Trait("Req", "LSRC-006")]
    public void RootsExistAndHoldOnlySource()
    {
        // LSRC-006: the home is the directory of the variable when it is set, else the repository
        var variable = Environment.GetEnvironmentVariable(LibrarySourceLayout.HomeVariable);
        var expectedHome = string.IsNullOrWhiteSpace(variable) ? RepoPaths.Root : Path.GetFullPath(variable);
        Assert.Equal(expectedHome, Layout.Home, ignoreCase: true);
        Assert.Equal(Path.Combine(Layout.Home, "rtl", "src"), Layout.RtlRoot);
        Assert.Equal(Path.Combine(Layout.Home, "ui-framework", "src"), Layout.FrameworkRoot);
        Assert.Equal(Path.Combine(Layout.Home, "data", "src"), Layout.DataRoot);

        var problems = new List<string>();
        foreach (var root in Layout.Roots)
        {
            if (!Directory.Exists(root))
            {
                problems.Add($"{Layout.Display(root)}: the directory is missing");
            }
            else if (!Directory.EnumerateFiles(root, "*.pas", SearchOption.TopDirectoryOnly).Any())
            {
                problems.Add($"{Layout.Display(root)}: holds no unit source (.pas)");
            }
        }

        foreach (var required in (string[])["System.pas", LibrarySourceLayout.AliasFile])
        {
            if (!File.Exists(Path.Combine(Layout.RtlRoot, required)))
            {
                problems.Add($"{Layout.Display(Path.Combine(Layout.RtlRoot, required))}: the file is missing");
            }
        }

        foreach (var file in Layout.AllFiles())
        {
            if (BinaryExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                problems.Add($"{Layout.Display(file)}: a compiled or linked file under a source directory");
            }
            else if (new FileInfo(file).Length == 0)
            {
                problems.Add($"{Layout.Display(file)}: the file is empty");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    [Trait("Req", "LSRC-002")]
    public void FileNamesMatchUnitNames()
    {
        var files = Layout.UnitFiles();
        Assert.NotEmpty(files);
        var problems = new List<string>();
        foreach (var file in files)
        {
            var expected = Path.GetFileNameWithoutExtension(file);
            var declared = LibrarySourceLayout.DeclaredUnitName(file);
            if (declared is null)
            {
                problems.Add($"{Layout.Display(file)}: does not start with a unit heading");
            }
            else if (!string.Equals(declared, expected, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{Layout.Display(file)}: declares unit '{declared}', expected '{expected}'");
            }
        }

        // one unit name, one file: the same name in two source directories would make one of them unreachable
        foreach (var group in files.GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add($"unit '{group.Key}' has more than one file: {string.Join(", ", group.Select(Layout.Display))}");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    [Trait("Req", "LSRC-003")]
    public void AliasesPointAtExistingUnits()
    {
        var path = Path.Combine(Layout.RtlRoot, LibrarySourceLayout.AliasFile);
        Assert.True(File.Exists(path), Layout.Display(path) + " is missing");
        var units = Layout.UnitFiles().Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(File.ReadAllBytes(path).AsMemory(HasByteOrderMark(path) ? 3 : 0));
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("aliases", out var aliases), "no member 'aliases'");
        Assert.Equal(JsonValueKind.Object, aliases.ValueKind);

        var problems = new List<string>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var alias in aliases.EnumerateObject())       // a repeated member is enumerated twice
        {
            count++;
            if (!keys.Add(alias.Name))
            {
                problems.Add($"alias '{alias.Name}' is listed more than once (names are compared ignoring case)");
            }

            if (alias.Value.ValueKind != JsonValueKind.String)
            {
                problems.Add($"alias '{alias.Name}': the value is not a string");
            }
            else if (!units.Contains(alias.Value.GetString()!))
            {
                problems.Add($"alias '{alias.Name}' names unit '{alias.Value.GetString()}', which has no .pas file in a source directory");
            }
        }

        Assert.True(count > 0, "the alias table is empty");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    [Trait("Req", "LSRC-004")]
    public void EveryResolvableUnitHasSource()
    {
        // every name a library file uses, the units themselves and the scoped names of the alias table
        var names = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Layout.UnitFiles())
        {
            names.TryAdd(Path.GetFileNameWithoutExtension(file), Layout.Display(file));
            var tree = SyntaxTree.Parse(SourceText.From(file, File.ReadAllText(file)), null, PhysicalFileSystem.Instance);
            foreach (var used in tree.Root.DescendantNodes().OfType<UsedUnitSyntax>())
            {
                Assert.True(used.InKeyword is null, $"{Layout.Display(file)}: a library unit names a file in its uses clause ({used.Name})");
                names.TryAdd(used.Name.ToString().Trim(), Layout.Display(file));
            }
        }

        var aliasFile = Path.Combine(Layout.RtlRoot, LibrarySourceLayout.AliasFile);
        using (var document = JsonDocument.Parse(File.ReadAllBytes(aliasFile).AsMemory(HasByteOrderMark(aliasFile) ? 3 : 0)))
        {
            foreach (var alias in document.RootElement.GetProperty("aliases").EnumerateObject())
            {
                names.TryAdd(alias.Name, LibrarySourceLayout.AliasFile);
            }
        }

        Assert.Contains("System", names.Keys);
        Assert.Contains("SysUtils", names.Keys);

        // the resolver of the build, with the default roots and no project: a main file that is not on disk, in a
        // directory that does not exist, so nothing but the roots can answer
        var probe = Path.Combine(Path.GetTempPath(), "lead-layout-" + Guid.NewGuid().ToString("N"), "LayoutProbe.pas");
        var compilation = Compilation.Create(
            new CompilationOptions
            {
                MainFile = probe,
                RtlDirectory = Layout.RtlRoot,
                SearchPaths = [Layout.FrameworkRoot, Layout.DataRoot],
                UsePcuCache = false,
            },
            [SourceText.From(probe, "program LayoutProbe;\nbegin\nend.\n")],
            PhysicalFileSystem.Instance);

        var problems = new List<string>();
        foreach (var (name, usedIn) in names)
        {
            var unit = compilation.FindUnit(name);
            if (unit is null)
            {
                problems.Add($"unit '{name}' (named in {usedIn}) does not resolve from the library roots");
            }
            else if (unit.FromPcu || !Path.GetExtension(unit.Path).Equals(".pas", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"unit '{name}' (named in {usedIn}) resolves to '{unit.Path}', which is not source");
            }
            else if (!Layout.IsUnderRoot(unit.Path) || !File.Exists(unit.Path))
            {
                problems.Add($"unit '{name}' (named in {usedIn}) resolves to '{unit.Path}', outside the library source directories");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    [Trait("Req", "LSRC-005")]
    public void IncludeFilesExist()
    {
        var problems = new List<string>();
        var directives = 0;
        foreach (var file in Layout.AllFiles().Where(f => Path.GetExtension(f).ToLowerInvariant() is ".pas" or ".inc"))
        {
            foreach (var include in IncludeDirectives(file))
            {
                directives++;
                var target = include.Argument.Trim().Trim('\'');
                if (target.Length == 0 || !File.Exists(Path.Combine(Path.GetDirectoryName(file)!, target)))
                {
                    problems.Add($"{Layout.Display(file)}: include file '{target}' does not exist next to the including file");
                }
            }
        }

        // the runtime library is known to use include files: a count of zero means the directives are no longer seen
        Assert.True(directives > 0, "no include directive was found in the library source");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static IEnumerable<DirectiveTriviaInfo> IncludeDirectives(string file)
    {
        var tokens = Lexer.Tokenize(SourceText.From(file, File.ReadAllText(file)));
        return tokens.SelectMany(t => t.LeadingTrivia.Concat(t.TrailingTrivia))
            .Where(t => t.Kind == SyntaxKind.IncludeDirectiveTrivia && t.Directive is not null)
            .Select(t => t.Directive!);
    }

    private static bool HasByteOrderMark(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> head = stackalloc byte[3];
        return stream.Read(head) == 3 && head is [0xEF, 0xBB, 0xBF];
    }
}
