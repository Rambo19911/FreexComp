using FreexComp.Compiler;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Tests.Infrastructure;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>D-163: the generated list of <see cref="SystemIdentifiers"/> is what the compiler binds as unit <c>System</c>.</summary>
[Trait("Phase", "8")]
public sealed class SystemIdentifiersTests
{
    [Fact(Timeout = 60000)]
    [Trait("Task", "D-163")]
    [Trait("Req", "UIF-727")]
    public void ListIsTheCompilersSystemScope()
    {
        // Everything visible in the statement part of an empty program that the built-in scope or unit System declares.
        var rtl = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        var main = Path.Combine(rtl, "D163Probe.pas"); // never written: an open file only
        const string text = "program D163Probe;\nbegin\nend.\n";
        var compilation = Compilation.Create(new CompilationOptions { MainFile = main, RtlDirectory = rtl }, [SourceText.From(main, text)],
            PhysicalFileSystem.Instance);
        var model = compilation.GetSemanticModel(main);
        var symbols = model.LookupSymbols(text.IndexOf("end.", StringComparison.Ordinal));
        bool IsSystem(Symbol s) => s.Kind != SymbolKind.Unit && (s.ContainingUnit is null || s.ContainingUnit.Name.Equals("System", StringComparison.OrdinalIgnoreCase));
        var expected = symbols.Where(IsSystem).Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // An alias (type HResult = Integer) is the aliased symbol itself: its name comes from the syntax tree of System.pas.
        var system = compilation.GetSemanticModel(Path.Combine(rtl, "System.pas")).SyntaxTree.Root;
        foreach (var type in system.DescendantNodes().OfType<TypeDeclarationSyntax>()
                     .Where(t => t.Parent is TypeSectionSyntax { Parent: InterfaceSectionSyntax }))
        {
            expected.Add(type.Identifier.Text);
        }

        // The built-in aliases (LongInt = Integer) have neither a symbol of their own nor syntax: a listed name the steps above
        // do not give must be a type that an empty program can use (it has nothing but System to find it in).
        var aliases = SystemIdentifiers.All.Where(n => !expected.Contains(n)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var probe = "program D163Probe;\nvar\n" + string.Concat(aliases.Select((n, i) => $"  V{i}: {n};\n")) + "begin\nend.\n";
        var errors = Compilation.Create(new CompilationOptions { MainFile = main, RtlDirectory = rtl }, [SourceText.From(main, probe)], PhysicalFileSystem.Instance)
            .GetDiagnostics(main, TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Code + " " + d.Message).ToList();
        Assert.True(errors.Count == 0, "listed names that are no type of System: " + string.Join("; ", errors));
        Assert.Equal(["Extended", "LongInt", "LongWord", "PWideChar", "Real", "TextFile", "UnicodeString", "WideChar"], aliases);
        expected.UnionWith(SystemIdentifiers.All);

        Assert.Contains("Exit", expected, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("TObject", expected, StringComparer.OrdinalIgnoreCase); // System.pas is bound, not only the built-ins
        var missing = expected.Except(SystemIdentifiers.All, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(missing.Count == 0, $"SystemIdentifiers lacks {string.Join(", ", missing)}; the list to paste:\n"
            + string.Join("\n", expected.Order(StringComparer.OrdinalIgnoreCase).Select(n => "        \"" + n + "\",")));
    }
}
