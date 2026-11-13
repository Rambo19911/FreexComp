using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Text;

/// <summary>Numbers of CRLF and lone-LF line breaks of a text. A lone CR is not counted (IDE-P1-075).</summary>
public readonly record struct LineEndingCounts(int CrLf, int Lf);

public static class LineEndingDetector
{
    public static LineEndingCounts Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int crlf = 0, lf = 0;
        var span = text.AsSpan();
        for (int i = span.IndexOf('\n'); i >= 0; i = NextLf(span, i))
        {
            if (i > 0 && span[i - 1] == '\r') crlf++;
            else lf++;
        }
        return new LineEndingCounts(crlf, lf);
    }

    private static int NextLf(ReadOnlySpan<char> span, int i)
    {
        int next = span[(i + 1)..].IndexOf('\n');
        return next < 0 ? -1 : i + 1 + next;
    }

    /// <summary>The kind new breaks use: CRLF when its count is at least the LF count; <paramref name="whenNone"/> without breaks.</summary>
    public static LineEndingKind Dominant(LineEndingCounts counts, LineEndingKind whenNone) =>
        counts is { CrLf: 0, Lf: 0 } ? whenNone : counts.CrLf >= counts.Lf ? LineEndingKind.CrLf : LineEndingKind.Lf;

    public static string NewLineText(LineEndingKind kind) => kind switch
    {
        LineEndingKind.CrLf => "\r\n",
        LineEndingKind.Lf => "\n",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "Mixed is not a line break."),
    };
}

/// <summary>
/// A document's line-ending state over its buffer (IDE-P1-075…077). <see cref="Kind"/> is computed from the buffer:
/// <see cref="LineEndingKind.Mixed"/> when both CRLF and LF occur; without any break it is the kind given at creation
/// (<c>files.defaultLineEnding</c>) or the last conversion. Handles <c>edit.convertLineEndings:crlf|lf</c> for the
/// document's command target. The buffer keeps every line's own ending; nothing is changed on load (IDE-P1-078).
/// </summary>
public sealed class LineEndingModel : ICommandTarget
{
    public const string ConvertCommandId = "edit.convertLineEndings";

    private LineEndingKind _whenNone;
    private LineEndingCounts _counts;
    private LineEndingKind _kind;

    public LineEndingModel(ITextBuffer buffer, LineEndingKind whenNone)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (whenNone == LineEndingKind.Mixed)
            throw new ArgumentOutOfRangeException(nameof(whenNone));
        Buffer = buffer;
        _whenNone = whenNone;
        _counts = LineEndingDetector.Count(buffer.Text);
        _kind = ComputeKind();
        buffer.Changed += (_, _) => Refresh(LineEndingDetector.Count(Buffer.Text));
    }

    public ITextBuffer Buffer { get; }

    /// <summary><c>CrLf</c>, <c>Lf</c> or <c>Mixed</c> (status-bar R6, info bar <c>Msg.File.MixedLineEndings</c>).</summary>
    public LineEndingKind Kind => _kind;

    /// <summary>Raised after <see cref="Kind"/> changed (edit, conversion, undo).</summary>
    public event EventHandler? KindChanged;

    /// <summary>The kind typed and pasted breaks use: <see cref="Kind"/>, or the dominant kind of a mixed document (IDE-P1-076).</summary>
    public LineEndingKind NewLineKind => _kind == LineEndingKind.Mixed ? LineEndingDetector.Dominant(_counts, _whenNone) : _kind;

    public string NewLine => LineEndingDetector.NewLineText(NewLineKind);

    /// <summary><paramref name="text"/> with every CRLF and LF replaced by <see cref="NewLine"/>; a lone CR stays.</summary>
    public string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", NewLine, StringComparison.Ordinal);
    }

    /// <summary>Typed or pasted text: replaces <paramref name="replacedLength"/> units at <paramref name="offset"/> with the normalized text, one undo step.</summary>
    public void Insert(int offset, string text, int replacedLength = 0) =>
        Buffer.ApplyEdits([new TextEdit(offset, replacedLength, Normalize(text))]);

    /// <summary>Rewrites every CRLF/LF break to <paramref name="target"/> in one undo step (IDE-P1-077); lone CRs stay.</summary>
    public void Convert(LineEndingKind target)
    {
        string newLine = LineEndingDetector.NewLineText(target);
        string text = Buffer.Text;
        var edits = new List<TextEdit>();
        for (int i = text.IndexOf('\n'); i >= 0; i = text.IndexOf('\n', i + 1))
        {
            bool crlf = i > 0 && text[i - 1] == '\r';
            if (crlf && target == LineEndingKind.Lf) edits.Add(new TextEdit(i - 1, 2, newLine));
            else if (!crlf && target == LineEndingKind.CrLf) edits.Add(new TextEdit(i, 1, newLine));
        }
        _whenNone = target;
        if (edits.Count > 0)
            Buffer.ApplyEdits(edits, Strings.Cmd_edit_convertLineEndings);
        else
            Refresh(_counts);
    }

    public bool CanExecute(string commandId, string? argument) =>
        commandId == ConvertCommandId && ParseTarget(argument) is not null && !Buffer.IsReadOnly;

    public Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        if (!CanExecute(commandId, argument))
            return Task.FromResult(false);
        Convert(ParseTarget(argument)!.Value);
        return Task.FromResult(true);
    }

    private static LineEndingKind? ParseTarget(string? argument) => argument switch
    {
        "crlf" => LineEndingKind.CrLf,
        "lf" => LineEndingKind.Lf,
        _ => null,
    };

    private LineEndingKind ComputeKind() =>
        _counts.CrLf > 0 && _counts.Lf > 0 ? LineEndingKind.Mixed : LineEndingDetector.Dominant(_counts, _whenNone);

    private void Refresh(LineEndingCounts counts)
    {
        _counts = counts;
        var kind = ComputeKind();
        if (kind == _kind)
            return;
        _kind = kind;
        KindChanged?.Invoke(this, EventArgs.Empty);
    }
}
