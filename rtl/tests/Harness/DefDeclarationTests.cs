using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL-812: the <c>.def</c> files and the <c>external</c> declarations of the shipped units match in both directions.
/// The units are parsed with <c>FreexComp.Compiler</c> (syntax only; <c>{$I}</c> includes are read).
/// </summary>
public sealed partial class DefFileTests
{
    /// <summary>Exports referenced by compiler-emitted code rather than by a declaration (ABI-161: the SEH personality routine).</summary>
    public static readonly string[] CompilerReferenced = ["__C_specific_handler"];

    /// <summary>The folders whose units are shipped (RTL-812: RTL, ui-framework and data units). Missing folders are skipped.</summary>
    public static IEnumerable<string> UnitDirectories =>
    [
        Path.Combine(RtlSetup.RepositoryRoot, "rtl", "src"),
        Path.Combine(RtlSetup.RepositoryRoot, "rtl", "winapi", "src"),
        Path.Combine(RtlSetup.RepositoryRoot, "ui-framework", "src"),
        Path.Combine(RtlSetup.RepositoryRoot, "data", "src"),
    ];

    /// <summary>
    /// RTL-812: every <c>external</c> declaration of a shipped unit has an entry in <c>&lt;dll&gt;.def</c>, and every
    /// <c>.def</c> entry is declared by some shipped unit. RTL-820: imports are <c>stdcall; external &lt;dllconst&gt; name
    /// '&lt;ExportName&gt;'</c>. RTL-802: an export is declared by one binding unit only (aliases inside that unit and the
    /// private re-declarations in <c>System</c>, RTL-801, are allowed).
    /// </summary>
    [Fact]
    [Trait("Kind", "Golden")]
    [Trait("Category", "Toolchain")]
    [Trait("Category", "Integration")]
    [Trait("Req", "RTL-812")]
    [Trait("Req", "RTL-820")]
    [Trait("Req", "RTL-802")]
    [Trait("Req", "RTL-002")]
    public void DefFilesMatchDeclarations()
    {
        var formatProblems = new List<string>();
        var exports = Directory.GetFiles(DefDirectory, "*.def")
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => Check(f, formatProblems), StringComparer.Ordinal);
        Assert.True(formatProblems.Count == 0, string.Join("\n", formatProblems));

        var units = UnitDirectories.Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.pas", SearchOption.AllDirectories))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => SourceText.From(Path.GetFullPath(f), File.ReadAllText(f)))
            .ToList();
        var problems = MatchDeclarations(exports, units, PhysicalFileSystem.Instance);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(units.Any(u => Path.GetFileName(u.Path) == "Windows.pas"), "rtl/winapi/src/Windows.pas was not scanned");
    }

    /// <summary>The checker finds each kind of mismatch (in-memory units and export lists).</summary>
    [Fact]
    [Trait("Req", "RTL-812")]
    public void DefFilesMatchDeclarations_ReportsMismatches()
    {
        var exports = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["kernel32"] = ["Declared", "FromUsed", "Listed", "Twice"],
            ["ntdll"] = ["__C_specific_handler"],
        };
        var a = SourceText.From(@"C:\u\A.pas", """
            unit A;
            interface
            const
              kernel32 = 'kernel32.dll';
              other = 'other.dll';
            function Declared: Integer; stdcall; external kernel32 name 'Declared';
            function Alias: Integer; stdcall; external kernel32 name 'Declared';
            function Missing: Integer; stdcall; external kernel32 name 'Missing';
            function NoDef: Integer; stdcall; external other name 'NoDef';
            function Literal: Integer; stdcall; external 'kernel32.dll' name 'Declared';
            function NoName: Integer; stdcall; external kernel32;
            function NoConv: Integer; external kernel32 name 'Declared';
            function Twice: Integer; stdcall; external kernel32 name 'Twice';
            implementation
            end.
            """);
        var b = SourceText.From(@"C:\u\B.pas", """
            unit B;
            interface
            implementation
            const
              kernel32 = 'kernel32.dll';
            function Twice: Integer; stdcall; external kernel32 name 'Twice';
            end.
            """);

        // the DLL-name constant may come from the interface of a used unit (CommCtrl uses Windows' comctl32), not from another unit
        var c = SourceText.From(@"C:\u\C.pas", """
            unit C;
            interface
            uses A;
            function FromUsed: Integer; stdcall; external kernel32 name 'FromUsed';
            implementation
            end.
            """);
        var d = SourceText.From(@"C:\u\D.pas", """
            unit D;
            interface
            uses B;
            function NotVisible: Integer; stdcall; external kernel32 name 'NotVisible';
            implementation
            end.
            """);

        var problems = MatchDeclarations(exports, [a, b, c, d], new InMemoryFileSystem());

        Assert.Equal(
        [
            "A.pas: Literal: the library must be a DLL-name constant, not a literal (RTL-820)",
            "A.pas: Missing: 'Missing' is not listed in kernel32.def",
            "A.pas: NoConv: not declared stdcall (RTL-820)",
            "A.pas: NoDef: no other.def for library 'other.dll'",
            "A.pas: NoName: no \"name '<ExportName>'\" (RTL-820)",
            "D.pas: NotVisible: library 'kernel32' is not a string constant of this unit or of a unit it uses",
            "kernel32.def: 'Listed' has no external declaration in a shipped unit",
            "kernel32.def: 'Twice' is declared by more than one binding unit (A.pas, B.pas; RTL-802)",
        ], problems);
    }

    /// <summary>All mismatches between <paramref name="exports"/> (def base name → entries) and the units' declarations, sorted.</summary>
    public static List<string> MatchDeclarations(IReadOnlyDictionary<string, List<string>> exports, IEnumerable<SourceText> units, IFileSystem fs)
    {
        var problems = new List<string>();
        var declaredBy = new Dictionary<(string Def, string Name), SortedSet<string>>();
        var parsed = units.Select(u => (Unit: u, Tree: SyntaxTree.Parse(u, null, fs))).ToList();

        // string constants: all of a unit's own, and the interface ones of each unit it names in a uses clause
        static IEnumerable<(string Name, string Value)> StringConstants(SyntaxNode node) =>
            node.DescendantNodes().OfType<ConstDeclarationSyntax>()
                .Where(c => c.Type is null && c.Value is LiteralExpressionSyntax { Token.Value: string })
                .Select(c => ((string)c.Identifier.Value!, (string)((LiteralExpressionSyntax)c.Value).Token.Value!));
        var exported = parsed.Where(p => p.Tree.Root is UnitSyntax)
            .ToDictionary(p => Path.GetFileNameWithoutExtension(p.Unit.Path), p => StringConstants(((UnitSyntax)p.Tree.Root).InterfaceSection).ToList(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var (unit, tree) in parsed)
        {
            var file = Path.GetFileName(unit.Path);
            var used = tree.Root.DescendantNodes().OfType<UsedUnitSyntax>().Select(u => u.Name.ToString().Trim())
                .SelectMany(n => exported.TryGetValue(n, out var list) ? list : []);
            var dllConstants = StringConstants(tree.Root).Concat(used)
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

            foreach (var routine in tree.Root.DescendantNodes().OfType<RoutineDeclarationSyntax>())
            {
                var external = routine.Directives.FirstOrDefault(d => d.Keyword.ContextualKind == SyntaxKind.ExternalKeyword);
                if (external is null)
                {
                    continue;
                }

                var routineName = routine.Heading.Name.ToString().Trim();
                void Problem(string text) => problems.Add($"{file}: {routineName}: {text}");

                if (!routine.Directives.Any(d => d.Keyword.ContextualKind == SyntaxKind.StdCallKeyword))
                {
                    Problem("not declared stdcall (RTL-820)");
                }

                string? library = null;
                switch (external.Value)
                {
                    case IdentifierNameSyntax id when dllConstants.TryGetValue((string)id.Identifier.Value!, out var value):
                        library = value;
                        break;
                    case LiteralExpressionSyntax { Token.Value: string literal }:
                        library = literal;
                        Problem("the library must be a DLL-name constant, not a literal (RTL-820)");
                        break;
                    default:
                        Problem($"library '{external.Value}' is not a string constant of this unit or of a unit it uses");
                        break;
                }

                if (external.NameValue is not LiteralExpressionSyntax { Token.Value: string exportName })
                {
                    Problem("no \"name '<ExportName>'\" (RTL-820)");
                    continue;
                }

                if (library is null)
                {
                    continue;
                }

                var def = Path.GetFileNameWithoutExtension(library).ToLowerInvariant();
                if (!exports.TryGetValue(def, out var names))
                {
                    Problem($"no {def}.def for library '{library}'");
                    continue;
                }

                if (!names.Contains(exportName, StringComparer.Ordinal))
                {
                    Problem($"'{exportName}' is not listed in {def}.def");
                    continue;
                }

                if (!declaredBy.TryGetValue((def, exportName), out var files))
                {
                    declaredBy[(def, exportName)] = files = new SortedSet<string>(StringComparer.Ordinal);
                }

                files.Add(file);
            }
        }

        foreach (var (def, names) in exports)
        {
            foreach (var name in names.Where(n => !CompilerReferenced.Contains(n, StringComparer.Ordinal)))
            {
                if (!declaredBy.TryGetValue((def, name), out var files))
                {
                    problems.Add($"{def}.def: '{name}' has no external declaration in a shipped unit");
                }
                else if (files.Count(f => !string.Equals(f, "System.pas", StringComparison.OrdinalIgnoreCase)) > 1)
                {
                    problems.Add($"{def}.def: '{name}' is declared by more than one binding unit ({string.Join(", ", files)}; RTL-802)");
                }
            }
        }

        problems.Sort(StringComparer.Ordinal);
        return problems;
    }
}
