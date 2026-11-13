using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler;

public enum DiagnosticSeverity { Hint, Warning, Error }

public sealed record RelatedLocation(Location Location, string Message);

/// <summary>One diagnostic; text and JSON forms are fixed by docs/interfaces/compiler-cli.md §2.</summary>
public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    Location Location,
    ImmutableArray<RelatedLocation> Related)
{
    /// <summary>Template arguments (for quick fixes).</summary>
    public ImmutableArray<object> Arguments { get; init; } = [];

    /// <summary>Builds a diagnostic from the catalogue entry: default severity, template filled with <paramref name="args"/>.</summary>
    public static Diagnostic Create(string code, Location location, params object[] args)
    {
        var descriptor = DiagnosticCatalog.Get(code);
        return new Diagnostic(code, descriptor.DefaultSeverity, descriptor.Format(args), location, [])
        {
            Arguments = [.. args],
        };
    }

    /// <summary><c>&lt;path&gt;(&lt;line&gt;,&lt;col&gt;): &lt;severity&gt; &lt;code&gt;: &lt;message&gt;</c>, 1-based.</summary>
    public string ToText()
    {
        var start = Location.LineSpan.Start;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Location.Path}({start.Line + 1},{start.Column + 1}): {SeverityText(Severity)} {Code}: {Message}");
    }

    /// <summary>One-line JSON object; <c>endLine</c>/<c>endCol</c> are the exclusive end (CMP-503).</summary>
    public string ToJson()
    {
        var sb = new StringBuilder();
        var start = Location.LineSpan.Start;
        var end = Location.LineSpan.End;
        sb.Append("{\"file\":");
        AppendString(sb, Location.Path);
        sb.Append(CultureInfo.InvariantCulture, $",\"line\":{start.Line + 1},\"col\":{start.Column + 1}");
        sb.Append(CultureInfo.InvariantCulture, $",\"endLine\":{end.Line + 1},\"endCol\":{end.Column + 1}");
        sb.Append(",\"severity\":");
        AppendString(sb, SeverityText(Severity));
        sb.Append(",\"code\":");
        AppendString(sb, Code);
        sb.Append(",\"message\":");
        AppendString(sb, Message);
        sb.Append(",\"related\":[");
        for (var i = 0; i < Related.Length; i++)
        {
            var r = Related[i];
            var rs = r.Location.LineSpan.Start;
            sb.Append(i == 0 ? "{\"file\":" : ",{\"file\":");
            AppendString(sb, r.Location.Path);
            sb.Append(CultureInfo.InvariantCulture, $",\"line\":{rs.Line + 1},\"col\":{rs.Column + 1},\"message\":");
            AppendString(sb, r.Message);
            sb.Append('}');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    internal static string SeverityText(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => "hint",
    };

    // Minimal JSON string escaping: quote, backslash and control characters; everything else verbatim.
    private static void AppendString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case < ' ': sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}"); break;
                default: sb.Append(c); break;
            }
        }

        sb.Append('"');
    }
}

public sealed record DiagnosticDescriptor(string Code, string? Name, DiagnosticSeverity DefaultSeverity, string Template)
{
    /// <summary>Fills the <c>%s</c>/<c>%d</c> placeholders left to right (CMP-502), invariant culture.</summary>
    /// <exception cref="ArgumentException">The argument count differs from the placeholder count.</exception>
    public string Format(params object[] args)
    {
        var sb = new StringBuilder(Template.Length + 16);
        var next = 0;
        for (var i = 0; i < Template.Length; i++)
        {
            if (Template[i] == '%' && i + 1 < Template.Length && Template[i + 1] is 's' or 'd')
            {
                if (next >= args.Length)
                {
                    throw new ArgumentException($"{Code}: too few arguments for '{Template}'", nameof(args));
                }

                sb.Append(Convert.ToString(args[next++], CultureInfo.InvariantCulture));
                i++;
            }
            else
            {
                sb.Append(Template[i]);
            }
        }

        if (next != args.Length)
        {
            throw new ArgumentException($"{Code}: too many arguments for '{Template}'", nameof(args));
        }

        return sb.ToString();
    }
}
