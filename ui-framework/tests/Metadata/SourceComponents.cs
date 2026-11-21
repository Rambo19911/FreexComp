using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.MetadataTesting;

/// <summary>
/// One published property of a source class mapped to metadata terms (metadata-authoring.md §2.2): <see cref="Type"/> is the
/// metadata <c>type</c>, <see cref="Kind"/> the metadata <c>kind</c> (<c>object</c>/<c>component</c>/<c>collection</c> or null).
/// Events have <see cref="IsEvent"/> and <see cref="Type"/> = the method-pointer type name (the metadata <c>signature</c>).
/// </summary>
public sealed record SourceProperty(string Name, string Type, string? Kind, bool IsEvent, PropertySymbol Symbol);

/// <summary>A <c>TComponent</c> descendant declared in a unit's interface section.</summary>
public sealed record SourceClass(string Name, string? Ancestor, string Unit, ClassTypeSymbol Symbol);

/// <summary>
/// The Pascal side of <c>MetadataMatchesSource</c> (metadata-authoring.md §4): parses units with <c>FreexComp.Compiler</c>
/// and lists components with their effective published properties (RTL-402 order: ancestor first, a redeclaration keeps the
/// ancestor's position).
/// </summary>
public sealed class SourceComponents
{
    /// <summary>Built-in metadata types that keep their Pascal name (component-metadata.md §3).</summary>
    private static readonly string[] NamedOrdinals = ["TColor", "TCursor", "TShortCut"];

    private readonly Compilation compilation;
    private readonly string sourceDirectory;
    private readonly string rtlDirectory;

    public SourceComponents(string sourceDirectory, string rtlDirectory)
    {
        this.sourceDirectory = sourceDirectory;
        this.rtlDirectory = rtlDirectory;
        var main = Directory.EnumerateFiles(sourceDirectory, "*.pas").Order(StringComparer.Ordinal).First();
        compilation = Compilation.Create(
            new CompilationOptions { MainFile = main, SearchPaths = [sourceDirectory], RtlDirectory = rtlDirectory },
            [],
            PhysicalFileSystem.Instance);
    }

    public string UnitPath(string unit) => Path.Combine(sourceDirectory, unit + ".pas");

    /// <summary>Errors of parsing/binding <paramref name="unit"/> (a unit that does not compile cannot be compared).</summary>
    public ImmutableArray<Diagnostic> Errors(string unit) =>
        [.. compilation.GetDiagnostics(UnitPath(unit)).Where(d => d.Severity == DiagnosticSeverity.Error)];

    /// <summary>Every <c>TComponent</c> descendant declared in the interface of <paramref name="unit"/>, in declaration order.</summary>
    public ImmutableArray<SourceClass> Components(string unit)
    {
        var model = compilation.GetSemanticModel(UnitPath(unit));
        return [.. model.Unit.InterfaceMembers.OfType<ClassTypeSymbol>()
            .Where(c => !c.IsForward && IsComponent(c))
            .DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c.Locations.IsEmpty ? 0 : c.Locations[0].Span.Start)
            .Select(c => new SourceClass(c.Name, c.Parent?.Name, unit, c))];
    }

    /// <summary>A class declared in the interface of an RTL unit (e.g. <c>TBasicAction</c> of Classes), or null.</summary>
    public ClassTypeSymbol? FindClass(string unit, string name)
    {
        var path = Path.Combine(rtlDirectory, unit + ".pas");
        return File.Exists(path)
            ? compilation.GetSemanticModel(path).Unit.InterfaceMembers.OfType<ClassTypeSymbol>()
                .FirstOrDefault(c => !c.IsForward && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    public static bool IsComponent(ClassTypeSymbol c) => c.SelfAndAncestors().Any(a => a.Name == "TComponent");

    /// <summary>The published properties declared (or redeclared) by <paramref name="c"/> itself, in declaration order.</summary>
    public static ImmutableArray<SourceProperty> Own(ClassTypeSymbol c) =>
        [.. c.Properties.Where(p => p.Visibility == Visibility.Published && !p.IsClassProperty && !p.IsArrayProperty).Select(Map)];

    /// <summary>
    /// The effective published list of <paramref name="c"/> (events and non-events together) without <c>TComponent</c>'s
    /// <c>Name</c> and <c>Tag</c> (built into the schema, metadata-authoring.md §3).
    /// </summary>
    public static ImmutableArray<SourceProperty> Effective(ClassTypeSymbol c)
    {
        var list = new List<SourceProperty>();
        foreach (var k in c.SelfAndAncestors().Reverse())
        {
            if (k.Name == "TComponent")
            {
                continue;
            }

            foreach (var p in Own(k))
            {
                var at = list.FindIndex(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                if (at >= 0)
                {
                    list[at] = p;
                }
                else
                {
                    list.Add(p);
                }
            }
        }

        return [.. list];
    }

    /// <summary>metadata-authoring.md §2.2: the metadata <c>type</c> and <c>kind</c> of a Pascal property type.</summary>
    public static SourceProperty Map(PropertySymbol p)
    {
        var t = p.Type;
        if (t is MethodPointerTypeSymbol)
        {
            return new(p.Name, t.Name, null, true, p);
        }

        if (NamedOrdinals.Contains(t.Name, StringComparer.Ordinal))
        {
            return new(p.Name, t.Name, null, false, p);
        }

        switch (t.TypeKind)
        {
            case TypeKind.Boolean:
                return new(p.Name, "Boolean", null, false, p);
            case TypeKind.Char:
                return new(p.Name, "Char", null, false, p);
            case TypeKind.Real:
                return new(p.Name, "Double", null, false, p);
            case TypeKind.String:
            case TypeKind.ShortString:
                return new(p.Name, "string", null, false, p);
            case TypeKind.Enum:
            case TypeKind.Set:
                return new(p.Name, t.Name, null, false, p);
            case TypeKind.Integer:
            case TypeKind.Subrange:
                if (t is EnumTypeSymbol)
                {
                    return new(p.Name, t.Name, null, false, p);                   // enumeration subrange (TBorderStyle)
                }

                return new(p.Name, t.Size == 8 ? "Int64" : "Integer", null, false, p);
            case TypeKind.Class when t is ClassTypeSymbol k:
                if (k.SelfAndAncestors().Any(a => a.Name == "TStrings"))
                {
                    return new(p.Name, "TStrings", null, false, p);
                }

                var kind = k.SelfAndAncestors().Any(a => a.Name == "TComponent") ? "component"
                    : k.SelfAndAncestors().Any(a => a.Name == "TCollection") ? "collection"
                    : "object";
                return new(p.Name, k.Name, kind, false, p);
            default:
                return new(p.Name, "?" + t.Name, null, false, p);                 // unmapped: always a mismatch
        }
    }
}
