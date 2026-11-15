using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>Bookmarks (I-121, editor-features.md §11 IDE-ED-110…114).</summary>
[Trait("Phase", "3")]
public sealed class BookmarkTests
{
    private const string Dir = @"C:\p\Hello";
    private static readonly string Text = string.Concat(Enumerable.Range(1, 10).Select(i => "line " + i.ToString(CultureInfo.InvariantCulture) + "\r\n"));

    private readonly InMemoryFileSystem _fs = new();
    private readonly IdeServices _services;

    public BookmarkTests()
    {
        _services = TestComposition.Build(o => o.FileSystem = _fs);
        _fs.CreateDirectory(Dir);
        _fs.WriteAllBytes(Dir + @"\A.pas", Encoding.UTF8.GetBytes(Text));
        _fs.WriteAllBytes(Dir + @"\B.pas", Encoding.UTF8.GetBytes(Text));
        _fs.WriteAllBytes(Dir + @"\Hello.pproj", Encoding.UTF8.GetBytes("""
            { "formatVersion": 1, "name": "Hello", "mainSource": "A.pas", "files": [ { "path": "A.pas" }, { "path": "B.pas" } ] }
            """));
    }

    private async Task<TextDocumentViewModel> OpenAsync(string name)
    {
        var document = Assert.IsType<TextDocumentViewModel>((await _services.Documents.OpenAsync(Path.Combine(Dir, name))).Value);
        _services.FocusTracker.Update(new FocusState(CommandScope.Editor, null, document, false));
        return document;
    }

    private Task RunAsync(string command, TextDocumentViewModel document, int line)
    {
        document.ViewState.Caret = new TextPosition(line, 3);
        return _services.Commands.ExecuteAsync(command);
    }

    private static string? Glyph(TextDocumentViewModel document, int line) =>
        document.Decorations.GlyphsAt(line).FirstOrDefault(g => g.Kind == LineGlyphKind.Bookmark)?.ProbeName;

    private static int[] Lines(TextDocumentViewModel document) =>
        [.. Enumerable.Range(1, document.Buffer.LineCount).Where(l => Glyph(document, l) is not null)];

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-110")]
    [Trait("Req", "IDE-ED-111")]
    [Trait("Req", "IDE-ED-112")]
    [Trait("Req", "IDE-ED-113")]
    [Trait("Req", "IDE-ED-114")]
    public async Task ToggleNumberedGotoAndAnchoring()
    {
        Assert.True(await _services.Projects.OpenAsync(Dir + @"\Hello.pproj"));
        var a = await OpenAsync("A.pas");

        // IDE-ED-111: toggle adds an unnumbered bookmark, toggles it off again; toggle<N> sets / moves / removes N.
        await RunAsync("search.bookmark.toggle", a, 2);
        Assert.Equal("Bookmark", Glyph(a, 2));
        await RunAsync("search.bookmark.toggle", a, 2);
        Assert.Null(Glyph(a, 2));
        await RunAsync("search.bookmark.toggle", a, 2);
        await RunAsync("search.bookmark.toggle3", a, 2); // replaces the unnumbered one on that line (one per line)
        Assert.Equal("Bookmark3", Glyph(a, 2));
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Editor_BookmarkSet, 3, 2), _services.Status.Message);
        await RunAsync("search.bookmark.toggle3", a, 5); // moves (each number exists once)
        Assert.Equal([5], Lines(a));
        await RunAsync("search.bookmark.toggle3", a, 5);
        Assert.Empty(Lines(a));

        // IDE-ED-112: goTo<N> opens the document and puts the caret at column 1 (history entry); unset → status.
        await RunAsync("search.bookmark.toggle1", a, 7);
        var b = await OpenAsync("B.pas");
        await RunAsync("search.bookmark.toggle", b, 4);
        await RunAsync("search.bookmark.toggle", b, 9);
        await RunAsync("search.bookmark.toggle2", b, 1);
        var moves = new List<NavigatedEventArgs>();
        _services.Navigation.Navigated += (_, e) => moves.Add(e);
        await _services.Documents.CloseAsync(a);
        await _services.Commands.ExecuteAsync("search.bookmark.goTo1");
        var reopened = Assert.IsType<TextDocumentViewModel>(_services.Documents.ActiveDocument);
        Assert.Equal(Dir + @"\A.pas", reopened.FilePath);
        Assert.Equal(new TextPosition(7, 1), reopened.ViewState.Caret);
        Assert.Equal("Bookmark1", Glyph(reopened, 7)); // kept while the document was closed
        Assert.Same(reopened, moves[^1].Document); // the jump inside A.pas is a navigation-history entry
        await _services.Commands.ExecuteAsync("search.bookmark.goTo5");
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Editor_BookmarkMissing, 5), _services.Status.Message);

        // next / previous: the active document's bookmarks in line order, wrapping.
        _services.Documents.Activate(b);
        _services.FocusTracker.Update(new FocusState(CommandScope.Editor, null, b, false));
        b.ViewState.Caret = new TextPosition(5, 2);
        var visited = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            await _services.Commands.ExecuteAsync("search.bookmark.next");
            visited.Add(b.ViewState.Caret.Line);
        }

        await _services.Commands.ExecuteAsync("search.bookmark.previous");
        visited.Add(b.ViewState.Caret.Line);
        Assert.Equal([9, 1, 4, 9, 4], visited);

        // IDE-ED-113: anchored to the line. A line inserted above moves it down; joining keeps the upper line's;
        // deleting the whole line including its break deletes it.
        b.Buffer.ApplyEdits([new TextEdit(0, 0, "new\r\n")]); // 1 → 2, 4 → 5, 9 → 10
        Assert.Equal([2, 5, 10], Lines(b));
        Assert.Equal("Bookmark2", Glyph(b, 2));
        await RunAsync("search.bookmark.toggle", b, 6);
        var (line5, length5, _) = b.Buffer.GetLine(5);
        b.Buffer.ApplyEdits([new TextEdit(line5 + length5, 2, "")]); // join 5 and 6: the bookmark of 5 stays
        Assert.Equal([2, 5, 9], Lines(b));
        var (line2, length2, delimiter2) = b.Buffer.GetLine(2);
        b.Buffer.ApplyEdits([new TextEdit(line2, length2 + delimiter2, "")]); // delete line 2 entirely
        Assert.Equal([4, 8], Lines(b));
        await _services.Commands.ExecuteAsync("search.bookmark.goTo2");
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Editor_BookmarkMissing, 2), _services.Status.Message);

        // IDE-ED-114: stored in the session and restored with the project.
        Assert.True(await _services.Documents.SaveAsync(b));
        _services.Session.Save();
        var session = Encoding.UTF8.GetString(_fs.ReadAllBytes(Dir + @"\.freexcomp\session.json"));
        Assert.Contains("\"bookmarks\"", session, StringComparison.Ordinal);
        Assert.True(await _services.Projects.CloseAsync());
        var again = TestComposition.Build(o => o.FileSystem = _fs);
        Assert.True(await again.Projects.OpenAsync(Dir + @"\Hello.pproj"));
        var restoredB = Assert.IsType<TextDocumentViewModel>((await again.Documents.OpenAsync(Dir + @"\B.pas")).Value);
        Assert.Equal([4, 8], Lines(restoredB));
        Assert.Equal("Bookmark", Glyph(restoredB, 4));
        var restoredA = Assert.IsType<TextDocumentViewModel>((await again.Documents.OpenAsync(Dir + @"\A.pas")).Value);
        Assert.Equal("Bookmark1", Glyph(restoredA, 7));

        // clearAll removes every bookmark of every document, without asking.
        await again.Commands.ExecuteAsync("search.bookmark.clearAll");
        Assert.Empty(Lines(restoredA));
        Assert.Empty(Lines(restoredB));
    }
}
