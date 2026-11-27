using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>Bracket and keyword pairing of editor-features.md §7 over the lexer tokens (IDE-ED-070…072).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Unit")]
public sealed class BracketMatchTests
{
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

    /// <summary>
    /// IDE-ED-075 (I-314): conditional-directive groups and <c>{$REGION}</c> pairs. Each result lists the highlighted
    /// directives in document order as <c>text@line</c> (0-based), "unmatched" for a directive drawn alone.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-ED-075")]
    public void ConditionalDirectivesPair()
    {
        string[] lines =
        [
            "{$IFDEF A}", // 0
            "x := 1;",
            "{$IF Defined(B)}", // 2: nested group
            "y := 2;",
            "{$IFEND}", // 4
            "{$ELSE}", // 5
            "(*$IFOPT R+*)z := 3;(*$ENDIF*)", // 6: the (*$…*) form
            "{$endif}", // 7: names are case-insensitive
            "{$ENDIF}", // 8: stray closer
            "{$REGION 'R'}", // 9
            "{$ENDREGION}", // 10
            "s := '{$IFDEF S}';", // 11: inside a string, no token
            "{$IFDEF U}", // 12: never closed
        ];
        var text = string.Join("\r\n", lines);
        var tokens = new PascalLanguage().ScanCodeTokens(text);
        int Offset(int line) => lines.Take(line).Sum(l => l.Length + 2);
        string At(int line, int column)
        {
            if (tokens.MatchAt(Offset(line) + column) is not { } match)
                return "none";
            string Show(CodeToken t) => text.Substring(t.Offset, t.Length) + "@" + text[..t.Offset].Count(c => c == '\n');
            return string.Join(" ", match.Highlighted.Select(Show)) + (match.Partner is null ? " unmatched" : "");
        }

        // The outer group from each of its members: at the start, inside, at the end.
        const string outer = "{$IFDEF A}@0 {$ELSE}@5 {$endif}@7";
        Assert.Equal(outer, At(0, 0));
        Assert.Equal(outer, At(0, 4));
        Assert.Equal(outer, At(5, 3));
        Assert.Equal(outer, At(7, 8));

        // The nested group and the (*$…*) group are separate; brackets inside a directive are no tokens.
        Assert.Equal("{$IF Defined(B)}@2 {$IFEND}@4", At(2, 13));
        Assert.Equal("{$IF Defined(B)}@2 {$IFEND}@4", At(4, 0));
        Assert.Equal("(*$IFOPT R+*)@6 (*$ENDIF*)@6", At(6, 5));
        Assert.Equal("(*$IFOPT R+*)@6 (*$ENDIF*)@6", At(6, 25));

        // {$REGION} pairs.
        Assert.Equal("{$REGION 'R'}@9 {$ENDREGION}@10", At(9, 2));
        Assert.Equal("{$REGION 'R'}@9 {$ENDREGION}@10", At(10, 12));

        // Without a partner: alone (drawn with the error outline).
        Assert.Equal("{$ENDIF}@8 unmatched", At(8, 3));
        Assert.Equal("{$IFDEF U}@12 unmatched", At(12, 5));

        // Not a directive: code between the directives, a directive in a string.
        Assert.Equal("none", At(1, 3));
        Assert.Equal("none", At(11, 9));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-070")]
    [Trait("Req", "IDE-ARCH-081")]
    public Task LargeBufferIsScannedInTheBackground() => TestUiThread.RunAsync(() => LargeBufferIsScannedInTheBackgroundOnUiThread()); // I-209

    private async Task LargeBufferIsScannedInTheBackgroundOnUiThread()
    {
        var text = string.Concat(Enumerable.Repeat("begin F(x); end;\r\n", 12_000)); // 216 000 chars
        Assert.True(text.Length > WholeTextClassifier.BackgroundThreshold);
        var buffer = new StringTextBuffer(text);
        var index = new CodeTokenIndex(buffer, new PascalLanguage());
        var arrived = new TaskCompletionSource();
        index.Changed += (_, _) => arrived.TrySetResult();

        // Not scanned on the calling thread: nothing until Changed.
        Assert.Null(index.Current);
        var deadline = CurrentTest.Deadline;
        await deadline.WaitAsync(arrived.Task, "the first background scan");
        Assert.Equal(12, index.Current!.MatchAt(0)!.Value.Partner!.Value.Offset); // "begin" … "end"

        // An edit outdates the tokens; GetAsync waits for the current version without blocking.
        buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        Assert.Null(index.Current);
        var fresh = await deadline.WaitAsync(index.GetAsync(), "the scan of the edited version");
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
