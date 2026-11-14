using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>Bracket and keyword pairing of editor-features.md §7 over the lexer tokens (IDE-ED-070…072).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Unit")]
public sealed class BracketMatchTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>The highlighted tokens at the caret <c>|</c> of <paramref name="marked"/> as <c>text@offset</c>, partner last.</summary>
    private static string Match(ILanguageDefinition language, string marked)
    {
        var caret = marked.IndexOf('|', StringComparison.Ordinal);
        var text = marked.Remove(caret, 1);
        if (language.ScanCodeTokens(text).MatchAt(caret) is not { } match)
            return "none";
        string Show(CodeToken t) => text.Substring(t.Offset, t.Length) + "@" + t.Offset;
        return match.Partner is { } partner ? Show(match.Bracket) + " " + Show(partner) : Show(match.Bracket) + " unmatched";
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-070")]
    [Trait("Req", "IDE-ED-071")]
    [Trait("Req", "UI-ED-053")]
    public void PairsIgnoreStringsAndComments()
    {
        var pascal = new PascalLanguage();
        const string code = "begin\r\n  s := '(' + F(a[1], { ) } (* ] *) b);\r\nend;";

        // Brackets inside the string and the comments are not tokens: the call's parentheses pair.
        Assert.Equal("(@21 )@43", Match(pascal, code.Replace("F(", "F|(", StringComparison.Ordinal)));
        Assert.Equal(")@43 (@21", Match(pascal, code.Replace("b)", "b)|", StringComparison.Ordinal)));
        Assert.Equal("]@25 [@23", Match(pascal, code.Replace("1]", "1]|", StringComparison.Ordinal)));
        Assert.Equal("none", Match(pascal, code.Replace("'('", "'|('", StringComparison.Ordinal)));
        Assert.Equal("none", Match(pascal, code.Replace("{ ) }", "{ |) }", StringComparison.Ordinal)));
        Assert.Equal("none", Match(pascal, code.Replace("(* ]", "(* |]", StringComparison.Ordinal)));

        // UI-ED-053: a bracket on either side of the caret; with one on both sides, the one before it wins.
        Assert.Equal(")@3 (@1", Match(pascal, "F(a)|(b);"));
        Assert.Equal("(@4 )@6", Match(pascal, "F(a)(|b);"));

        // No partner: only the bracket itself.
        Assert.Equal(")@6 unmatched", Match(pascal, "x := a)|;"));
        Assert.Equal("[@1 unmatched", Match(pascal, "a|[1;"));

        // IDE-ED-071: keyword pairs (IDE-ED-052 pairing), with the caret inside or at either end of the word.
        Assert.Equal("begin@0 end@47", Match(pascal, code.Replace("begin", "beg|in", StringComparison.Ordinal)));
        Assert.Equal("end@47 begin@0", Match(pascal, code.Replace("end;", "end|;", StringComparison.Ordinal)));
        Assert.Equal("repeat@0 until@19", Match(pascal, "re|peat\r\n  x := 1;\r\nuntil y;"));
        Assert.Equal("until@19 repeat@0", Match(pascal, "repeat\r\n  x := 1;\r\n|until y;"));
        Assert.Equal("try@0 end@25", Match(pascal, "|try\r\n  A;\r\nexcept\r\n  B;\r\nend;"));
        Assert.Equal("class@12 end@38", Match(pascal, "type\r\n  T = cl|ass\r\n    X: Integer;\r\n  end;"));
        Assert.Equal("asm@0 end@12", Match(pascal, "as|m\r\n  nop\r\nend;"));
        Assert.Equal("end@0 unmatched", Match(pascal, "e|nd;"));
        Assert.Equal("none", Match(pascal, "x := be|gun;")); // an identifier
        Assert.Equal("none", Match(pascal, "{ be|gin } end;")); // a comment
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-070")]
    public void SearchIsLimitedTo20000Tokens()
    {
        var pascal = new PascalLanguage();
        string Words(int n) => string.Join(" ", Enumerable.Repeat("a", n));
        Assert.Equal("(@0 )@" + (1 + Words(19_999).Length), Match(pascal, "|(" + Words(19_999) + ")"));
        Assert.Equal("(@0 unmatched", Match(pascal, "|(" + Words(20_000) + ")"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-070")]
    public void FormAndJsonBracketsPair()
    {
        Assert.Equal("]@11 [@6", Match(new JsonLanguage(), """{"a": [1, 2]|, "b": "[x"}"""));
        Assert.Equal("none", Match(new JsonLanguage(), """{"a": "|["}"""));
        Assert.Equal("{@0 }@12", Match(new JsonLanguage(), """|{"a": [1, 2]}"""));
        Assert.Equal("(@10 )@21", Match(new FormLanguage(), "  Items = |(\r\n    'a)')"));
        Assert.Equal("none", Match(new PlainTextLanguage(), "|(a)"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-070")]
    [Trait("Req", "IDE-ARCH-081")]
    public async Task LargeBufferIsScannedInTheBackground()
    {
        var text = string.Concat(Enumerable.Repeat("begin F(x); end;\r\n", 12_000)); // 216 000 chars
        Assert.True(text.Length > WholeTextClassifier.BackgroundThreshold);
        var buffer = new StringTextBuffer(text);
        var index = new CodeTokenIndex(buffer, new PascalLanguage());
        var arrived = new TaskCompletionSource();
        index.Changed += (_, _) => arrived.TrySetResult();

        // Not scanned on the calling thread: nothing until Changed.
        Assert.Null(index.Current);
        await arrived.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);
        Assert.Equal(12, index.Current!.MatchAt(0)!.Value.Partner!.Value.Offset); // "begin" … "end"

        // An edit outdates the tokens; GetAsync waits for the current version without blocking.
        buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        Assert.Null(index.Current);
        var fresh = await index.GetAsync().WaitAsync(Limit, TestContext.Current.CancellationToken);
        Assert.Equal(buffer.Version, fresh.Version);
        Assert.Null(fresh.MatchAt(0)); // "xbegin" is an identifier
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-070")]
    public void SmallBufferIsScannedSynchronously()
    {
        var buffer = new StringTextBuffer("F(x);");
        var index = new CodeTokenIndex(buffer, new PascalLanguage());
        Assert.NotNull(index.Current?.MatchAt(1));
        buffer.ApplyEdits([new TextEdit(0, 0, "G")]);
        Assert.Equal(2, index.Current!.MatchAt(2)!.Value.Bracket.Offset);
    }
}
