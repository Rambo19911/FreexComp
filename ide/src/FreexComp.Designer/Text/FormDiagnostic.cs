using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Text;

/// <summary>
/// A form diagnostic (form-document.md §3; codes and texts of form-format.md §5). Positions are 1-based, columns in
/// UTF-16 code units (DSG-FD-030).
/// </summary>
public sealed record FormDiagnostic(string Code, DiagnosticSeverity Severity, string Message,
    string Path, int Line, int Col, int EndLine, int EndCol, string? ObjectName, string? PropertyPath)
{
    /// <summary><c>file(line,col): severity CODE: message</c> (compiler-cli.md §2).</summary>
    public override string ToString() =>
        $"{Path}({Line},{Col}): {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
}

/// <summary>The outcome of <see cref="FormParser.Parse"/>.</summary>
public sealed record FormParseResult(FormDocument? Document, ImmutableArray<FormDiagnostic> Diagnostics)
{
    /// <summary>True → <see cref="Document"/> is null (DSG-FD-025).</summary>
    public bool HasSyntaxErrors => Document is null;
}
