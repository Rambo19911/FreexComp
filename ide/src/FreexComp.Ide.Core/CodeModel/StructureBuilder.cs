using System.Collections.Immutable;
using System.Globalization;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Ide.Core.Resources;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.CodeModel;

/// <summary>Icon kind of a Structure row (UI-TW-203).</summary>
public enum StructureKind { Group, Error, Unit, Class, Record, Interface, Enum, Type, Field, Constant, Routine, Property, Component }

/// <summary>
/// A Structure row (tool-windows.md §3): <see cref="Key"/> per UI-TW-209, <see cref="Text"/> per UI-TW-202,
/// <see cref="NameOffset"/> = the start of the declaration's name (or the error position), <see cref="Start"/>/<see cref="End"/>
/// = the declaration span (caret tracking, UI-TW-205). <see cref="Visibility"/> = <c>private</c>, <c>protected</c>, … for
/// members (null = none / public); <see cref="Position"/> = <c>line:col</c> of an error row. Design view (UI-TW-207):
/// <see cref="IsNonVisual"/> = a non-visual component (gear overlay), <see cref="IsUnavailable"/> = an unknown or read-only
/// (preserved) object (<c>text.disabled</c>, warning overlay), <see cref="InheritedFrom"/> = the frame or ancestor class of an
/// inherited object (UI-DSG-136, 143), <see cref="InheritsFrom"/> = the ancestor class of a descendant's root row (UI-DSG-144),
/// <see cref="IsDesignRoot"/> = the form root (the <c>form</c> glyph, UI-TW-1610), <see cref="PaletteIcon"/> = the full path of
/// the component's palette icon (DSG-TP-009; null: the generic component glyph, UI-TW-1610).
/// </summary>
public sealed record StructureNode(string Key, string Name, string Text, StructureKind Kind, int NameOffset, int Start, int End,
    IReadOnlyList<StructureNode> Children, string? Visibility = null, string? Position = null, bool IsNonVisual = false, bool IsUnavailable = false,
    string? InheritedFrom = null, string? InheritsFrom = null, bool IsDesignRoot = false, string? PaletteIcon = null);

/// <summary>The Structure tree of a Pascal syntax tree (UI-TW-200…209, IDE-ED-130), from <c>NavigationService.GetDocumentSymbols</c>.</summary>
public static class StructureBuilder
{
    private const int MaxConstantText = 40;

    /// <param name="sorted">UI-TW-206: children of every node except the top-level groups and Errors by name.</param>
    public static IReadOnlyList<StructureNode> Build(SyntaxTree tree, bool sorted, IReadOnlyList<Diagnostic>? extraDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var uses = new List<DocumentSymbol>();
        var types = new List<DocumentSymbol>();
        var constants = new List<DocumentSymbol>();
        var variables = new List<DocumentSymbol>();
        var routines = new List<DocumentSymbol>();
        void Collect(IEnumerable<DocumentSymbol> symbols)
        {
            foreach (var s in symbols)
            {
                switch (s.Kind)
                {
                    case CompletionItemKind.Keyword: // interface / implementation sections
                        Collect(s.Children);
                        break;
                    case CompletionItemKind.Unit when s.Name == "uses":
                        uses.AddRange(s.Children);
                        break;
                    case CompletionItemKind.Unit:
                        Collect(s.Children); // the unit/program root
                        break;
                    case CompletionItemKind.Class or CompletionItemKind.Interface or CompletionItemKind.Type or CompletionItemKind.Enum:
                        types.Add(s);
                        break;
                    case CompletionItemKind.Constant:
                        constants.Add(s);
                        break;
                    case CompletionItemKind.Variable:
                        variables.Add(s);
                        break;
                    case CompletionItemKind.Routine or CompletionItemKind.Method:
                        routines.Add(s);
                        break;
                }
            }
        }

        Collect(NavigationService.GetDocumentSymbols(tree));
        var text = tree.Text.Text;
        var groups = new List<StructureNode>();
        var errors = tree.Diagnostics.Concat(extraDiagnostics ?? [])
            .Where(d => d.Severity == DiagnosticSeverity.Error && string.Equals(d.Location.Path, tree.Text.Path, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(d => (d.Code, d.Location.Span.Start, d.Message))
            .OrderBy(d => d.Location.Span.Start)
            .ToList();
        if (errors.Count > 0)
        {
            groups.Add(Group("Errors", string.Format(CultureInfo.CurrentCulture, Strings.Structure_Group_Errors, errors.Count),
                [.. errors.Select((d, i) => new StructureNode("Errors/" + i.ToString(CultureInfo.InvariantCulture), d.Message, d.Message, StructureKind.Error,
                    d.Location.Span.Start, d.Location.Span.Start, d.Location.Span.End, [],
                    Position: string.Create(CultureInfo.InvariantCulture, $"{d.Location.LineSpan.Start.Line + 1}:{d.Location.LineSpan.Start.Column + 1}")))]));
        }

        AddGroup(groups, "Uses", Strings.Structure_Group_Uses, uses, sorted, text);
        AddGroup(groups, "Types", Strings.Structure_Group_Types, types, sorted, text);
        AddGroup(groups, "Constants", Strings.Structure_Group_Constants, constants, sorted, text);
        AddGroup(groups, "Variables", Strings.Structure_Group_Variables, variables, sorted, text);
        AddGroup(groups, "Routines", Strings.Structure_Group_Routines, routines, sorted, text);
        if (Exports(tree, sorted) is { Count: > 0 } exports)
            groups.Add(Group("Exports", Strings.Structure_Group_Exports, exports));
        return groups;
    }

    /// <summary>
    /// IDE-TGT-018: one row per item of the <c>exports</c> clauses, as written (name with its <c>index</c> and <c>name</c>
    /// specifiers), navigable to the item.
    /// </summary>
    private static List<StructureNode> Exports(SyntaxTree tree, bool sorted)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nodes = new List<StructureNode>();
        foreach (var item in tree.Root.DescendantNodes().OfType<ExportsClauseSyntax>().SelectMany(c => c.Items))
        {
            if (item.Name.IsMissing)
                continue;
            var name = Slice(tree.Text.Text, item.Name.Span.Start, item.Name.Span.End);
            var count = seen.GetValueOrDefault(name);
            seen[name] = count + 1;
            nodes.Add(new StructureNode("Exports/" + name + (count > 0 ? "#" + count.ToString(CultureInfo.InvariantCulture) : ""), name,
                Collapse(Slice(tree.Text.Text, item.Span.Start, item.Span.End)), StructureKind.Routine, item.Name.Span.Start, item.Span.Start, item.Span.End, []));
        }

        return sorted ? [.. nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)] : nodes;
    }

    /// <summary>UI-TW-205: the innermost declaration whose span contains <paramref name="offset"/> (errors excluded).</summary>
    public static StructureNode? FindInnermost(IReadOnlyList<StructureNode> nodes, int offset)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        foreach (var node in nodes.Where(n => n.Kind != StructureKind.Error && n.Key != "Errors"))
        {
            if (node.Kind == StructureKind.Group)
            {
                if (FindInnermost(node.Children, offset) is { } inGroup)
                    return inGroup;
                continue;
            }

            if (offset >= node.Start && offset <= node.End)
                return FindInnermost(node.Children, offset) ?? node;
        }

        return null;
    }

    private static StructureNode Group(string key, string title, IReadOnlyList<StructureNode> children) =>
        new(key, title, title, StructureKind.Group, -1, -1, -1, children);

    private static void AddGroup(List<StructureNode> groups, string key, string title, List<DocumentSymbol> symbols, bool sorted, string text)
    {
        if (symbols.Count > 0)
            groups.Add(Group(key, title, Nodes(key, symbols, null, sorted, text)));
    }

    private static List<StructureNode> Nodes(string parentKey, IEnumerable<DocumentSymbol> symbols, string? visibility, bool sorted, string text)
    {
        var flat = new List<(DocumentSymbol Symbol, string? Visibility)>();
        foreach (var s in symbols)
        {
            if (s.Kind == CompletionItemKind.Keyword) // a visibility section: its members, marked with it
                flat.AddRange(s.Children.Select(c => (c, (string?)s.Name)));
            else
                flat.Add((s, visibility));
        }

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nodes = new List<StructureNode>();
        foreach (var (symbol, vis) in flat)
        {
            var count = seen.GetValueOrDefault(symbol.Name);
            seen[symbol.Name] = count + 1;
            var key = parentKey + "/" + symbol.Name + (count > 0 ? "#" + count.ToString(CultureInfo.InvariantCulture) : "");
            var children = symbol.Kind is CompletionItemKind.Routine or CompletionItemKind.Method && parentKey == "Routines"
                ? []
                : Nodes(key, symbol.Children, null, sorted, text);
            nodes.Add(new StructureNode(key, symbol.Name, RowText(symbol, text), KindOf(symbol), symbol.SelectionSpan.Start, symbol.Span.Start, symbol.Span.End,
                children, vis is "public" or "published" ? null : vis));
        }

        // Overload keys are numbered in source order before sorting (UI-TW-209).
        return sorted ? [.. nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)] : nodes;
    }

    private static StructureKind KindOf(DocumentSymbol s) => s.Kind switch
    {
        CompletionItemKind.Unit => StructureKind.Unit,
        CompletionItemKind.Class => StructureKind.Class,
        CompletionItemKind.Interface => StructureKind.Interface,
        CompletionItemKind.Enum => StructureKind.Enum,
        CompletionItemKind.Type when s.Detail.StartsWith("record", StringComparison.OrdinalIgnoreCase) => StructureKind.Record,
        CompletionItemKind.Type => StructureKind.Type,
        CompletionItemKind.Constant or CompletionItemKind.EnumMember => StructureKind.Constant,
        CompletionItemKind.Routine or CompletionItemKind.Method => StructureKind.Routine,
        CompletionItemKind.Property => StructureKind.Property,
        _ => StructureKind.Field,
    };

    /// <summary>UI-TW-202: declarations as written, one line, single spaces.</summary>
    private static string RowText(DocumentSymbol s, string text)
    {
        switch (s.Kind)
        {
            case CompletionItemKind.Unit or CompletionItemKind.EnumMember:
                return s.Name;
            case CompletionItemKind.Class or CompletionItemKind.Interface or CompletionItemKind.Type or CompletionItemKind.Enum:
                return s.Name + " = " + (s.Kind == CompletionItemKind.Enum ? Collapse(Slice(text, s.SelectionSpan.End, s.Span.End)).TrimStart('=', ' ').TrimEnd(';') : s.Detail);
            case CompletionItemKind.Routine or CompletionItemKind.Method:
            {
                var heading = s.Detail;
                var at = heading.IndexOf(s.Name, StringComparison.OrdinalIgnoreCase);
                var row = (at >= 0 ? heading[at..] : heading).Trim().TrimEnd(';').Trim();
                return row;
            }
            case CompletionItemKind.Constant:
            {
                var declaration = Slice(text, s.SelectionSpan.End, s.Span.End);
                var eq = declaration.IndexOf('=', StringComparison.Ordinal);
                var value = Collapse(eq >= 0 ? declaration[(eq + 1)..] : declaration).TrimEnd(';').Trim();
                if (value.Length > MaxConstantText)
                    value = value[..MaxConstantText] + "…";
                return s.Name + " = " + value;
            }
            default:
                return s.Detail.Length > 0 ? s.Name + ": " + s.Detail : s.Name;
        }
    }

    private static string Slice(string text, int start, int end) =>
        start >= 0 && end <= text.Length && end > start ? text[start..end] : "";

    private static string Collapse(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
