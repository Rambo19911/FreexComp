using System.Globalization;
using System.Text;
using FreexComp.Compiler.Syntax;

namespace FreexComp.Ide.Core.Editing;

/// <summary>A code template (IDE-ED-060): abbreviation (unique per language, case-insensitive), description, language, body.</summary>
public sealed record CodeTemplate(string Abbreviation, string Description, string Language, string Body);

/// <summary>A field of an expansion: its name and the occurrences (offset, length) relative to the expansion; the first is the edit point.</summary>
public sealed record TemplateField(string Name, IReadOnlyList<(int Offset, int Length)> Occurrences);

/// <summary>The text of an expanded template, its fields in order of first occurrence and the <c>$end$</c> offset.</summary>
public sealed record TemplateExpansion(string Text, IReadOnlyList<TemplateField> Fields, int EndOffset);

/// <summary>IDE-ED-060: expands a template body.</summary>
public static class CodeTemplateExpander
{
    /// <param name="lineIndent">The indentation of the line where the template is expanded (prefixed to every later line).</param>
    /// <param name="indentUnit">One indent unit (a leading tab of a body line).</param>
    /// <param name="newLine">The document's line ending.</param>
    /// <param name="selection">The text for <c>$selection$</c> (Surround With, P8+).</param>
    /// <param name="evaluate">
    /// IDE-ED-152: the value of a template function (its canonical name, <see cref="TemplateFunctions.Names"/>), asked once per
    /// field whose default calls it; null = every function gives an empty text. A call of an unknown function stays literal.
    /// </param>
    public static TemplateExpansion Expand(string body, string lineIndent, string indentUnit, string newLine, string selection = "",
        Func<string, string>? evaluate = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in Placeholders(body))
        {
            if (value is not null && !defaults.ContainsKey(name))
                defaults[name] = value.StartsWith('=') && TemplateFunctions.Resolve(value) is { } function ? evaluate?.Invoke(function) ?? "" : value;
        }

        var text = new StringBuilder();
        var fields = new List<(string Name, List<(int, int)> Occurrences)>();
        int? end = null;
        var lines = body.ReplaceLineEndings("\n").Split('\n');
        for (var l = 0; l < lines.Length; l++)
        {
            var line = lines[l];
            if (l > 0)
            {
                text.Append(newLine).Append(lineIndent);
                var tabs = 0;
                while (tabs < line.Length && line[tabs] == '\t')
                    tabs++;
                for (var t = 0; t < tabs; t++)
                    text.Append(indentUnit);
                line = line[tabs..];
            }

            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] != '$')
                {
                    text.Append(line[i]);
                    continue;
                }

                if (i + 1 < line.Length && line[i + 1] == '$')
                {
                    text.Append('$');
                    i++;
                    continue;
                }

                var close = line.IndexOf('$', i + 1);
                if (close < 0 || ParsePlaceholder(line[(i + 1)..close]) is not { } placeholder)
                {
                    text.Append('$');
                    continue;
                }

                i = close;
                switch (placeholder.Name)
                {
                    case "end":
                        end ??= text.Length;
                        break;
                    case "selection":
                        text.Append(selection);
                        break;
                    default:
                        var value = defaults.GetValueOrDefault(placeholder.Name, "");
                        var field = fields.Find(f => f.Name == placeholder.Name);
                        if (field.Name is null)
                            fields.Add(field = (placeholder.Name, []));
                        field.Occurrences.Add((text.Length, value.Length));
                        text.Append(value);
                        break;
                }
            }
        }

        return new TemplateExpansion(text.ToString(), [.. fields.Select(f => new TemplateField(f.Name, f.Occurrences))], end ?? text.Length);
    }

    private static IEnumerable<(string Name, string? Default)> Placeholders(string body)
    {
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '$')
                continue;
            if (i + 1 < body.Length && body[i + 1] == '$')
            {
                i++;
                continue;
            }

            var close = body.IndexOf('$', i + 1);
            if (close < 0 || body.AsSpan(i + 1, close - i - 1).IndexOfAny('\n', '\r') >= 0 || ParsePlaceholder(body[(i + 1)..close]) is not { } p)
                continue;
            yield return p;
            i = close;
        }
    }

    /// <summary><c>name</c> or <c>name:default</c> with an identifier name; null when it is not a placeholder.</summary>
    private static (string Name, string? Default)? ParsePlaceholder(string inner)
    {
        var colon = inner.IndexOf(':', StringComparison.Ordinal);
        var name = colon < 0 ? inner : inner[..colon];
        if (!IsIdentifier(name))
            return null;
        return (name, colon < 0 ? null : inner[(colon + 1)..]);
    }

    /// <summary><c>[A-Za-z_][A-Za-z0-9_]*</c> (dialogs.md §9.3).</summary>
    public static bool IsIdentifier(string text) =>
        text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>The longest abbreviation (dialogs.md §9.3 <c>Abbreviation</c>, max 16).</summary>
    public const int MaxAbbreviationLength = 16;

    /// <summary>An identifier of at most <see cref="MaxAbbreviationLength"/> characters (dialogs.md §9.3).</summary>
    public static bool IsValidAbbreviation(string text) => IsIdentifier(text) && text.Length <= MaxAbbreviationLength;

    /// <summary>
    /// IDE-SET-083: the first syntax error of a body, or null. A <c>$</c> that is not part of <c>$$</c> must open a
    /// placeholder <c>$name$</c> / <c>$name:default$</c> closed on the same line (else <see cref="TemplateSyntaxErrorKind.Unterminated"/>).
    /// Positions are 1-based within the body.
    /// </summary>
    public static TemplateSyntaxError? FindSyntaxError(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var lines = body.ReplaceLineEndings("\n").Split('\n');
        for (var l = 0; l < lines.Length; l++)
        {
            var line = lines[l];
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] != '$')
                    continue;
                if (i + 1 < line.Length && line[i + 1] == '$')
                {
                    i++;
                    continue;
                }

                var close = line.IndexOf('$', i + 1);
                if (close < 0 || ParsePlaceholder(line[(i + 1)..close]) is not { } placeholder)
                    return new TemplateSyntaxError(TemplateSyntaxErrorKind.Unterminated, l + 1, i + 1);
                if (placeholder.Default is ['=', ..] call && TemplateFunctions.Resolve(call) is null)
                    return new TemplateSyntaxError(TemplateSyntaxErrorKind.UnknownFunction, l + 1, i + 1, TemplateFunctions.CalledName(call));
                i = close;
            }
        }

        return null;
    }
}

/// <summary>The kind of a template body syntax error (IDE-SET-083).</summary>
public enum TemplateSyntaxErrorKind
{
    /// <summary>A <c>$</c> that opens no placeholder closed on its line.</summary>
    Unterminated,

    /// <summary>A field default <c>=Name()</c> that does not call a function of <see cref="TemplateFunctions.Names"/> (IDE-ED-152).</summary>
    UnknownFunction,
}

/// <summary>
/// IDE-ED-152: the template functions a field default calls as <c>$name:=Func()$</c> (case-insensitive, no arguments); the
/// value is computed once at expansion and is the field's initial text.
/// </summary>
public static class TemplateFunctions
{
    /// <summary>The canonical names.</summary>
    public static IReadOnlyList<string> Names { get; } = ["UnitName", "ClassName", "Date", "Time"];

    /// <summary>The canonical name of the function <paramref name="call"/> (<c>=Name()</c>) calls; null when it is not a call of one.</summary>
    public static string? Resolve(string call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.Length < 3 || call[0] != '=' || !call.EndsWith("()", StringComparison.Ordinal))
            return null;
        var name = call[1..^2];
        return Names.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The name a call <c>=Name()</c> uses, for the syntax error (without the <c>=</c> and an empty <c>()</c>).</summary>
    internal static string CalledName(string call) => call.EndsWith("()", StringComparison.Ordinal) ? call[1..^2] : call[1..];

    /// <summary>
    /// <c>UnitName()</c>: the unit / program / library name of the syntax tree, else the file name without extension, else
    /// <c>Unit1</c>.
    /// </summary>
    public static string UnitName(SyntaxTree? tree, string? filePath)
    {
        var name = tree?.Root switch
        {
            UnitSyntax unit => unit.Name.ToString(),
            ProgramSyntax { Name: { } program } => program.ToString(),
            LibrarySyntax library => library.Name.ToString(),
            _ => null,
        };
        name = name is null ? null : string.Concat(name.Where(c => !char.IsWhiteSpace(c)));
        if (!string.IsNullOrEmpty(name))
            return name;
        return string.IsNullOrEmpty(filePath) ? "Unit1" : Path.GetFileNameWithoutExtension(filePath);
    }

    /// <summary>
    /// <c>ClassName()</c>: the class (or record) of the routine implementation containing <paramref name="offset"/>
    /// (<c>TFoo</c> for <c>procedure TFoo.Bar</c>), else the type declaration containing it, else empty.
    /// </summary>
    public static string ClassName(SyntaxTree tree, int offset)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var ancestors = tree.FindToken(Math.Clamp(offset, 0, tree.Root.FullSpan.End)).Parent?.AncestorsAndSelf().ToList() ?? [];
        if (ancestors.OfType<RoutineDeclarationSyntax>().FirstOrDefault(r => r.Heading.Name is QualifiedNameSyntax) is { Heading.Name: QualifiedNameSyntax qualified })
            return string.Concat(qualified.Left.ToString().Where(c => !char.IsWhiteSpace(c)));
        return ancestors.OfType<TypeDeclarationSyntax>().FirstOrDefault() is { Identifier: { IsMissing: false } identifier } ? identifier.Text : "";
    }

    /// <summary><c>Date()</c>: the local date <c>yyyy-MM-dd</c>.</summary>
    public static string Date(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary><c>Time()</c>: the local time <c>HH:mm</c>.</summary>
    public static string Time(DateTime local) => local.ToString("HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>A template body syntax error at a 1-based line and column of the body; <paramref name="Name"/> = the function name, if any.</summary>
public sealed record TemplateSyntaxError(TemplateSyntaxErrorKind Kind, int Line, int Column, string? Name = null);

/// <summary>
/// Field mode after an expansion (IDE-ED-065): the fields' occurrences as ranges that follow the edits, the current
/// field and the <c>$end$</c> position. An edit inside an occurrence grows or shrinks it; any other edit ends field mode.
/// </summary>
internal sealed class TemplateFieldMode
{
    private readonly List<List<int[]>> _fields; // per field, per occurrence: [start, end]
    private int _end;

    public TemplateFieldMode(int start, TemplateExpansion expansion)
    {
        _fields = [.. expansion.Fields.Select(f => f.Occurrences.Select(o => new[] { start + o.Offset, start + o.Offset + o.Length }).ToList())];
        _end = start + expansion.EndOffset;
    }

    public int Current { get; set; }

    public int Count => _fields.Count;

    public bool IsActive { get; private set; } = true;

    /// <summary>The buffer version after the last field edit (field edits join one undo step); -1 = none yet.</summary>
    public long LastEditVersion { get; set; } = -1;

    /// <summary>The field and occurrence the last edit was in, to mirror (null = none pending).</summary>
    public (int Field, int Occurrence)? Edited { get; set; }

    public bool Mirroring { get; set; }

    public int End => _end;

    /// <summary>The primary (first) occurrence of field <paramref name="index"/>.</summary>
    public (int Start, int End) Primary(int index) => (_fields[index][0][0], _fields[index][0][1]);

    public IEnumerable<(int Start, int End)> AllOccurrences => _fields.SelectMany(f => f.Select(o => (o[0], o[1])));

    public IReadOnlyList<(int Start, int End)> Occurrences(int field) => [.. _fields[field].Select(o => (o[0], o[1]))];

    public bool Contains(int offset) => _fields.Any(f => f.Any(o => o[0] <= offset && offset <= o[1]));

    public void Deactivate() => IsActive = false;

    /// <summary>Follows one buffer change; false when it was outside all fields (field mode ends).</summary>
    public bool OnChanged(int offset, int removed, int inserted)
    {
        var delta = inserted - removed;
        (int, int)? inside = null;
        for (var f = 0; f < _fields.Count && inside is null; f++)
        {
            for (var o = 0; o < _fields[f].Count; o++)
            {
                var range = _fields[f][o];
                if (range[0] <= offset && offset + removed <= range[1])
                {
                    inside = (f, o);
                    break;
                }
            }
        }

        if (inside is not { } hit)
            return false;
        var edited = _fields[hit.Item1][hit.Item2];
        var editStart = edited[0];
        foreach (var range in _fields.SelectMany(f => f))
        {
            if (ReferenceEquals(range, edited))
            {
                range[1] += delta;
            }
            else if (range[0] >= offset + removed)
            {
                range[0] += delta;
                range[1] += delta;
            }
        }

        if (_end >= offset + removed && !(removed == 0 && _end == offset && editStart == offset))
            _end += delta;
        if (!Mirroring)
            Edited = hit;
        return true;
    }
}
