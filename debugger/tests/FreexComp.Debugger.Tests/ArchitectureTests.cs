using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace FreexComp.Debugger.Tests;

/// <summary>DBG-002: the library uses the compiler for syntax only and has no UI, build or language-service dependency.</summary>
[Trait("Req", "DBG-002")]
public sealed class ArchitectureTests
{
    private const string CompilerAssembly = "FreexComp.Compiler";
    private static readonly string[] AllowedCompilerNamespaces = ["FreexComp.Compiler.Syntax", "FreexComp.Compiler.Text"];
    private static readonly string[] ForbiddenAssemblyPrefixes =
        ["FreexComp.LanguageService", "FreexComp.Build", "FreexComp.Ide", "Avalonia"];

    [Fact]
    public void UsesOnlyCompilerSyntaxNamespaces()
    {
        var path = System.Reflection.Assembly.Load("FreexComp.Debugger").Location;

        Assert.Empty(ForbiddenAssemblyReferences(path));
        Assert.Empty(CompilerTypesOutsideSyntax(path));
    }

    /// <summary>The scanner itself must see a forbidden use: this test assembly names a non-syntax compiler type.</summary>
    [Fact]
    public void Scanner_ReportsCompilerTypesOutsideSyntaxNamespaces()
    {
        Type[] used = [typeof(FreexComp.Compiler.Compilation), typeof(FreexComp.Compiler.Syntax.SyntaxFacts)];
        Assert.Equal(2, used.Length);

        var found = CompilerTypesOutsideSyntax(typeof(ArchitectureTests).Assembly.Location);

        Assert.Contains("FreexComp.Compiler.Compilation", found);
        Assert.DoesNotContain("FreexComp.Compiler.Syntax.SyntaxFacts", found);
    }

    private static List<string> ForbiddenAssemblyReferences(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        return md.AssemblyReferences
            .Select(handle => md.GetString(md.GetAssemblyReference(handle).Name))
            .Where(name => ForbiddenAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>Full names of all types of the compiler assembly that the given assembly references outside the allowed namespaces.</summary>
    private static List<string> CompilerTypesOutsideSyntax(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var result = new List<string>();
        foreach (var handle in md.TypeReferences)
        {
            // A nested type is scoped by its declaring type: walk out to the top-level type.
            var type = md.GetTypeReference(handle);
            while (type.ResolutionScope.Kind == HandleKind.TypeReference)
                type = md.GetTypeReference((TypeReferenceHandle)type.ResolutionScope);
            if (type.ResolutionScope.Kind != HandleKind.AssemblyReference)
                continue;
            var assembly = md.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope);
            if (md.GetString(assembly.Name) != CompilerAssembly)
                continue;
            var ns = md.GetString(type.Namespace);
            if (!AllowedCompilerNamespaces.Contains(ns))
                result.Add(ns + "." + md.GetString(type.Name));
        }

        return result.Distinct().Order(StringComparer.Ordinal).ToList();
    }
}
