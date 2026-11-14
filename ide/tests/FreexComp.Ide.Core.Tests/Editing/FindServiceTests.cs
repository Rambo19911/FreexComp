using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>In-editor find and replace on the buffer, without a view (phase1-shell.md §10, I-057).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Unit")]
public sealed class FindServiceTests
{
    private static readonly TimeSpan StatusGone = TimeSpan.FromSeconds(6);

    private static async Task<(IdeServices Services, TextDocumentViewModel Doc, ManualClock Clock)> OpenAsync(string text, string name = "A.pas")
    {
        var fs = new InMemoryFileSystem();
        var clock = new ManualClock();
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.Clock = clock;
        });
        fs.CreateDirectory(@"C:\t");
        fs.WriteAllBytes(@"C:\t\" + name, Encoding.UTF8.GetBytes(text));
        var opened = await services.Documents.OpenAsync(@"C:\t\" + name);
        Assert.True(opened.Success, opened.MessageId);
        var doc = Assert.IsType<TextDocumentViewModel>(opened.Value);
        services.FocusTracker.Update(new FocusState(CommandScope.Editor, null, doc, false)); // the editor has focus
        return (services, doc, clock);
    }

    private static TextMatch[] M(params (int Offset, int Length)[] matches) => [.. matches.Select(m => new TextMatch(m.Offset, m.Length))];

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-101")]
    [Trait("Req", "IDE-P1-106")]
    [Trait("Req", "IDE-ARCH-081")]
    public async Task IncrementalMatchesAndCounter()
    {
        var (services, doc, clock) = await OpenAsync("alpha beta alpha\r\nalpha\r\n");
        var find = services.Find;
        doc.ViewState.Caret = new TextPosition(1, 3); // the search origin: offset 2
        var session = find.For(doc);
        session.Open(replace: false);
        Assert.True(session.IsOpen);
        Assert.Equal("", session.CounterText);

        // Debounced 100 ms; then every match is listed and the first at or after the origin is selected.
        find.SearchText = "alpha";
        Assert.Empty(session.Matches);
        clock.Advance(FindSession.Delay);
        Assert.Equal(M((0, 5), (11, 5), (18, 5)), session.Matches);
        Assert.Equal(new TextSelection(11, 16), doc.Selection);
        Assert.Equal((2, "2 of 3"), (session.CurrentIndex, session.CounterText));

        // Each change searches again from the origin (not from the selected match).
        find.SearchText = "alph";
        clock.Advance(FindSession.Delay);
        Assert.Equal(new TextSelection(11, 15), doc.Selection);
        Assert.Equal("2 of 3", session.CounterText);

        // Navigation moves the origin; an edit keeps the list current.
        await services.Commands.ExecuteAsync("search.findNext");
        Assert.Equal(new TextSelection(18, 22), doc.Selection);
        Assert.Equal("3 of 3", session.CounterText);
        find.SearchText = "alpha";
        clock.Advance(FindSession.Delay);
        Assert.Equal(new TextSelection(18, 23), doc.Selection);
        doc.Buffer.ApplyEdits([new TextEdit(0, 0, "alpha ")]);
        clock.Advance(FindSession.Delay);
        Assert.Equal(M((0, 5), (6, 5), (17, 5), (24, 5)), session.Matches);

        // No match / empty text.
        find.SearchText = "zzz";
        clock.Advance(FindSession.Delay);
        Assert.Equal((0, Strings.Msg_Find_NoMatches), (session.Matches.Count, session.CounterText));
        find.SearchText = "";
        clock.Advance(FindSession.Delay);
        Assert.Equal((0, ""), (session.Matches.Count, session.CounterText));

        // Closing removes the matches and keeps the selection.
        find.SearchText = "beta";
        clock.Advance(FindSession.Delay);
        var selected = doc.Selection;
        session.Close();
        Assert.False(session.IsOpen);
        Assert.Empty(session.Matches);
        Assert.Equal(selected, doc.Selection);

        // The count stops at 10 000.
        var (many, manyDoc, manyClock) = await OpenAsync(string.Concat(Enumerable.Repeat("ab\n", 10_001)), "B.txt");
        var manySession = many.Find.For(manyDoc);
        manySession.Open(replace: false);
        many.Find.SearchText = "b";
        manyClock.Advance(FindSession.Delay);
        Assert.Equal((10_000, true), (manySession.Matches.Count, manySession.IsCapped));
        Assert.Equal("1 of 10000+", manySession.CounterText);

        // A buffer above 200 KB is searched on the thread pool, not in the debounce callback.
        var (large, largeDoc, largeClock) = await OpenAsync(string.Concat(Enumerable.Repeat("Foo := Bar;\r\n", 20_000)), "C.pas");
        Assert.True(largeDoc.Buffer.Length > WholeTextClassifier.BackgroundThreshold);
        var largeSession = large.Find.For(largeDoc);
        largeSession.Open(replace: false);
        var arrived = new TaskCompletionSource();
        largeSession.Changed += (_, _) =>
        {
            if (largeSession.Matches.Count > 0)
                arrived.TrySetResult();
        };
        large.Find.SearchText = "Bar";
        largeClock.Advance(FindSession.Delay);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(10_000, largeSession.Matches.Count); // capped
        Assert.Equal(new TextSelection(7, 10), largeDoc.Selection);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-102")]
    [Trait("Req", "IDE-P1-103")]
    public async Task OptionsCaseWordRegex()
    {
        var (services, doc, clock) = await OpenAsync("Foo foo food _foo\r\nbar$ end\r\nx\r\n");
        var find = services.Find;
        var session = find.For(doc);
        session.Open(replace: false);
        IReadOnlyList<TextMatch> Search(string text)
        {
            find.SearchText = text;
            clock.Advance(FindSession.Delay);
            return session.Matches;
        }

        // Case-insensitive by default; Match case; Whole word (no letter, digit or underscore around the match).
        Assert.Equal(M((0, 3), (4, 3), (8, 3), (14, 3)), Search("foo"));
        find.MatchCase = true;
        clock.Advance(FindSession.Delay);
        Assert.Equal(M((4, 3), (8, 3), (14, 3)), session.Matches);
        find.WholeWord = true;
        clock.Advance(FindSession.Delay);
        Assert.Equal(M((4, 3)), session.Matches);
        find.MatchCase = false;
        find.WholeWord = false;

        // Literal text: "$" is no anchor; text with a line break never matches.
        Assert.Equal(M((19, 4)), Search("bar$"));
        Assert.Empty(Search("x\r\n"));

        // Regex: ^/$ at line boundaries (CRLF too), "." never matches a line break, zero-length matches are skipped.
        find.Regex = true;
        Assert.Equal(M((19, 4)), Search(@"^bar\$"));
        Assert.Equal(M((24, 3)), Search("end$"));
        Assert.Empty(Search("end."));
        Assert.Equal(M((24, 3)), Search("e.d"));
        Assert.Empty(Search("^"));
        Assert.Equal(Strings.Msg_Find_NoMatches, session.CounterText);
        Assert.Equal(M((0, 3), (4, 3)), Search(@"\bfoo\b"));

        // Invalid pattern: the message, nothing highlighted.
        Assert.Empty(Search("(foo"));
        Assert.StartsWith(Strings.Msg_Find_RegexInvalid[..^3], session.Error, StringComparison.Ordinal);
        Search("foo");
        Assert.Null(session.Error);

        // A pattern that runs too long is stopped (1 s) with the timeout reason.
        doc.Buffer.ApplyEdits([new TextEdit(0, 0, new string('a', 100) + "\r\n")]);
        Assert.Empty(Search("(a|aa)+b"));
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Find_RegexInvalid, Strings.Reason_RegexTimeout), session.Error);

        // Match case, Whole word and Regex persist in uistate.json (find.*); In selection does not.
        find.MatchCase = true;
        Assert.Equal((true, false, true),
            (services.Layout.GetUiFlag("find", "matchCase"), services.Layout.GetUiFlag("find", "wholeWord"), services.Layout.GetUiFlag("find", "regex")));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-104")]
    public async Task WrapAroundMessages()
    {
        var (services, doc, clock) = await OpenAsync("one two one\r\n");
        var commands = services.Commands;
        Assert.False(commands.CanExecute("search.findNext")); // menus.md: a search text must exist
        services.Find.SearchText = "one";
        Assert.True(commands.CanExecute("search.findNext"));
        Assert.True(commands.CanExecute("search.findPrevious"));

        // Panel closed: the last search text; from the caret, wrapping with a status message.
        doc.ViewState.Caret = new TextPosition(1, 12);
        await commands.ExecuteAsync("search.findNext");
        Assert.Equal((new TextSelection(0, 3), Strings.Msg_Find_WrappedForward), (doc.Selection, services.Status.Message));
        clock.Advance(StatusGone);
        await commands.ExecuteAsync("search.findNext");
        Assert.Equal((new TextSelection(8, 11), (string?)null), (doc.Selection, services.Status.Message));
        await commands.ExecuteAsync("search.findPrevious");
        Assert.Equal((new TextSelection(0, 3), (string?)null), (doc.Selection, services.Status.Message));
        await commands.ExecuteAsync("search.findPrevious");
        Assert.Equal((new TextSelection(8, 11), Strings.Msg_Find_WrappedBackward), (doc.Selection, services.Status.Message));
        clock.Advance(StatusGone);

        // A single match wraps onto itself.
        services.Find.SearchText = "two";
        await commands.ExecuteAsync("search.findNext");
        Assert.Equal(new TextSelection(4, 7), doc.Selection);
        await commands.ExecuteAsync("search.findNext");
        Assert.Equal((new TextSelection(4, 7), Strings.Msg_Find_WrappedForward), (doc.Selection, services.Status.Message));
        clock.Advance(StatusGone);

        // No match: the status bar with the panel closed, the panel's counter when it is open.
        services.Find.SearchText = "zzz";
        await commands.ExecuteAsync("search.findNext");
        Assert.Equal((new TextSelection(4, 7), Strings.Msg_Find_NoMatches), (doc.Selection, services.Status.Message));
        clock.Advance(StatusGone);
        var session = services.Find.For(doc);
        doc.Selection = TextSelection.At(7); // a selected word would become the search text (IDE-P1-100)
        session.Open(replace: false);
        clock.Advance(FindSession.Delay);
        await commands.ExecuteAsync("search.findPrevious");
        Assert.Equal((Strings.Msg_Find_NoMatches, (string?)null), (session.CounterText, services.Status.Message));

        // Read-only documents are searched too (IDE-P1-109).
        doc.Buffer.IsReadOnly = true;
        services.Find.SearchText = "one";
        clock.Advance(FindSession.Delay);
        Assert.Equal(new TextSelection(8, 11), doc.Selection); // incremental: the first match after the origin
        await commands.ExecuteAsync("search.findNext");
        Assert.Equal(new TextSelection(0, 3), doc.Selection);
        Assert.False(session.CanReplace);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-107")]
    [Trait("Req", "IDE-P1-109")]
    public async Task ReplaceAndReplaceAllSingleUndo()
    {
        var (services, doc, clock) = await OpenAsync("cat dog cat\r\ncat\r\n");
        var find = services.Find;
        var session = find.For(doc);
        session.Open(replace: true);
        Assert.True(session.IsReplaceMode);
        find.SearchText = "cat";
        find.ReplaceText = "bird";
        clock.Advance(FindSession.Delay);
        Assert.Equal(new TextSelection(0, 3), doc.Selection);

        // The selection is a match: replaced, then the next match is selected.
        Assert.True(session.CanReplace);
        await session.ReplaceAsync();
        Assert.Equal("bird dog cat\r\ncat\r\n", doc.Buffer.Text);
        Assert.Equal(new TextSelection(9, 12), doc.Selection);

        // The selection is no match: only moves on.
        doc.Selection = TextSelection.At(1);
        await session.ReplaceAsync();
        Assert.Equal("bird dog cat\r\ncat\r\n", doc.Buffer.Text);
        Assert.Equal(new TextSelection(9, 12), doc.Selection);

        // Replace All: one undo step; the caret keeps its line and column.
        doc.Selection = TextSelection.At(doc.Buffer.GetOffset(2, 2));
        Assert.Equal(2, await session.ReplaceAllAsync());
        Assert.Equal("bird dog bird\r\nbird\r\n", doc.Buffer.Text);
        Assert.Equal(Strings.Msg_Find_ReplacedMany.Replace("{0}", "2", StringComparison.Ordinal), services.Status.Message);
        Assert.Equal(new TextPosition(2, 2), doc.ViewState.Caret);
        await services.Commands.ExecuteAsync("edit.undo");
        Assert.Equal("bird dog cat\r\ncat\r\n", doc.Buffer.Text);
        await services.Commands.ExecuteAsync("edit.redo");
        Assert.Equal("bird dog bird\r\nbird\r\n", doc.Buffer.Text);

        find.SearchText = "dog";
        Assert.Equal(1, await session.ReplaceAllAsync());
        Assert.Equal(Strings.Msg_Find_ReplacedOne, services.Status.Message);
        Assert.Equal(0, await session.ReplaceAllAsync());
        Assert.Equal(Strings.Msg_Find_ReplaceNone, services.Status.Message);

        // Read-only: no replacing (IDE-P1-109).
        find.SearchText = "bird";
        doc.Buffer.IsReadOnly = true;
        Assert.False(session.CanReplace);
        Assert.Equal(0, await session.ReplaceAllAsync());
        await session.ReplaceAsync();
        Assert.Equal("bird bird bird\r\nbird\r\n", doc.Buffer.Text);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-107")]
    public async Task RegexReplacementGroups()
    {
        var (services, doc, _) = await OpenAsync("a=1\r\nb=2\r\n");
        var find = services.Find;
        var session = find.For(doc);
        session.Open(replace: true);

        // $n, ${name}, $$ and the escapes \n (the document's line ending), \t, \\.
        find.Regex = true;
        find.SearchText = @"(\w)=(?<v>\d)";
        find.ReplaceText = @"${v}:$1$$\t\\\n";
        Assert.Equal(2, await session.ReplaceAllAsync());
        Assert.Equal("1:a$\t\\\r\n\r\n2:b$\t\\\r\n\r\n", doc.Buffer.Text);
        await services.Commands.ExecuteAsync("edit.undo");

        // Replace of a selected regex match uses the same substitution.
        find.SearchText = @"(\w)=(\d)";
        find.ReplaceText = "$2=$1";
        doc.Selection = new TextSelection(0, 3);
        await session.ReplaceAsync();
        Assert.Equal("1=a\r\nb=2\r\n", doc.Buffer.Text);
        Assert.Equal(new TextSelection(5, 8), doc.Selection);

        // Literal mode: the replacement is literal.
        find.Regex = false;
        find.SearchText = "=";
        find.ReplaceText = @"$1\t";
        Assert.Equal(2, await session.ReplaceAllAsync());
        Assert.Equal("1$1\\ta\r\nb$1\\t2\r\n", doc.Buffer.Text);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-102")]
    public async Task InSelectionRangeTracksEdits()
    {
        var (services, doc, clock) = await OpenAsync("x1\r\nx2\r\nx3\r\nx4\r\n");
        var find = services.Find;
        var session = find.For(doc);

        // A single-line selection does not offer In selection.
        doc.Selection = new TextSelection(4, 6);
        session.Open(replace: false);
        Assert.False(session.CanUseInSelection);
        Assert.False(session.InSelection);
        session.Close();

        // A multi-line selection (lines 2–3) turns it on and restricts the matches to that range.
        doc.Selection = new TextSelection(4, 12);
        session.Open(replace: true);
        Assert.True(session.CanUseInSelection);
        Assert.True(session.InSelection);
        find.SearchText = "x";
        clock.Advance(FindSession.Delay);
        Assert.Equal(M((4, 1), (8, 1)), session.Matches);

        // The range follows edits before and inside it.
        doc.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        doc.Buffer.ApplyEdits([new TextEdit(doc.Buffer.GetOffset(3, 1), 0, "xx")]);
        clock.Advance(FindSession.Delay);
        Assert.Equal("xx1\r\nx2\r\nxxx3\r\nx4\r\n", doc.Buffer.Text);
        Assert.Equal(M((5, 1), (9, 1), (10, 1), (11, 1)), session.Matches);
        Assert.Equal(new TextMatch(5, 10), session.SelectionRange);

        // Replace All stays inside it; turning the option off searches the whole document.
        find.ReplaceText = "y";
        Assert.Equal(4, await session.ReplaceAllAsync());
        Assert.Equal("xx1\r\ny2\r\nyyy3\r\nx4\r\n", doc.Buffer.Text);
        session.InSelection = false;
        clock.Advance(FindSession.Delay);
        Assert.Equal(M((0, 1), (1, 1), (15, 1)), session.Matches);

        // The option is not remembered (IDE-P1-102): the next opening without a multi-line selection has it off.
        session.Close();
        doc.Selection = TextSelection.At(0);
        session.Open(replace: false);
        Assert.False(session.InSelection);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-105")]
    public async Task FindSelectionNextWithoutPanel()
    {
        var (services, doc, _) = await OpenAsync("a.b x a.b axb\r\nword  here word\r\n");
        var find = services.Find;
        var session = find.For(doc);
        var commands = services.Commands;
        Assert.True(commands.CanExecute("search.findSelectionNext"));

        // The selection becomes the search text, searched literally even with Regex on, without opening the panel.
        find.Regex = true;
        doc.Selection = new TextSelection(0, 3);
        await commands.ExecuteAsync("search.findSelectionNext");
        Assert.Equal(new TextSelection(6, 9), doc.Selection);
        Assert.False(session.IsOpen);
        Assert.True(find.Regex);
        Assert.Equal(@"a\.b", find.SearchText);
        await commands.ExecuteAsync("search.findNext"); // the same search again: wraps, never matches "axb"
        Assert.Equal(new TextSelection(0, 3), doc.Selection);

        // An empty selection takes the word under the caret.
        find.Regex = false;
        doc.Selection = TextSelection.At(doc.Buffer.GetOffset(2, 3));
        await commands.ExecuteAsync("search.findSelectionNext");
        Assert.Equal("word", find.SearchText);
        Assert.Equal(new TextSelection(26, 30), doc.Selection);

        // Not on a word (between the two spaces): nothing happens.
        doc.Selection = TextSelection.At(20);
        await commands.ExecuteAsync("search.findSelectionNext");
        Assert.Equal("word", find.SearchText);
        Assert.Equal(TextSelection.At(20), doc.Selection);
    }
}
