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
    public Task ToggleNumberedGotoAndAnchoring() => TestUiThread.RunAsync(() => ToggleNumberedGotoAndAnchoringOnUiThread()); // I-209

    private async Task ToggleNumberedGotoAndAnchoringOnUiThread()
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

    private BookmarkInfo Single(int line) => Assert.Single(_services.Bookmarks.All, b => b.Line == line);

    /// <summary>
    /// IDE-ED-116 / IDE-ED-118 (I-315): a name is trimmed and cut at 80; <c>toggle&lt;N&gt;</c> keeps it; toggling the
    /// bookmark off removes it with its name; the session stores the name only when there is one and restores it; a
    /// session without names loads.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-ED-116")]
    [Trait("Req", "IDE-ED-118")]
    public Task NamedBookmarkSurvivesToggleNumberAndSession() => TestUiThread.RunAsync(NamedBookmarkSurvivesToggleNumberAndSessionOnUiThread);

    private async Task NamedBookmarkSurvivesToggleNumberAndSessionOnUiThread()
    {
        var bookmarks = _services.Bookmarks;
        Assert.True(await _services.Projects.OpenAsync(Dir + @"\Hello.pproj"));
        var a = await OpenAsync("A.pas");

        // Added through the service: unnumbered, no name; a second call keeps the bookmark of the line.
        var added = bookmarks.Ensure(a, 4);
        Assert.Equal((Dir + @"\A.pas", 4, (int?)null, (string?)null), (added.Path, added.Line, added.Number, added.Name));
        Assert.Equal("Bookmark", Glyph(a, 4));
        Assert.True(bookmarks.Rename(added, "  " + new string('x', 90) + "  "));
        Assert.Equal(new string('x', 80), Single(4).Name); // trimmed, cut at 80
        Assert.True(bookmarks.Rename(Single(4), "  entry\t"));
        Assert.Equal("entry", Single(4).Name);
        Assert.Equal("entry", bookmarks.Ensure(a, 4).Name);
        Assert.Single(bookmarks.All);
        Assert.Equal("entry", BookmarkService.NormalizeName("entry\r\nsecond line"));
        Assert.Null(BookmarkService.NormalizeName("   "));

        // toggle<N> on the line keeps the name and sets the number; another toggle<N> elsewhere moves the number and the
        // named bookmark stays without it.
        await RunAsync("search.bookmark.toggle3", a, 4);
        Assert.Equal((3, "entry"), (Single(4).Number, Single(4).Name));
        Assert.Equal("Bookmark3", Glyph(a, 4));
        await RunAsync("search.bookmark.toggle3", a, 8);
        Assert.Equal(((int?)null, "entry"), (Single(4).Number, Single(4).Name));
        Assert.Equal((3, (string?)null), (Single(8).Number, Single(8).Name));
        await RunAsync("search.bookmark.toggle3", a, 4);
        Assert.Equal((3, "entry"), (Single(4).Number, Single(4).Name));
        Assert.DoesNotContain(bookmarks.All, b => b.Line == 8); // unnamed: moved as before (IDE-ED-111)

        // An empty name means no name.
        await RunAsync("search.bookmark.toggle", a, 6);
        Assert.True(bookmarks.Rename(Single(6), "other"));
        Assert.True(bookmarks.Rename(Single(6), "  "));
        Assert.Null(Single(6).Name);

        // IDE-ED-118: the name is written only when there is one, and restored with the project.
        Assert.True(await _services.Documents.SaveAsync(a));
        _services.Session.Save();
        var session = System.Text.Json.Nodes.JsonNode.Parse(_fs.ReadAllBytes(Dir + @"\.freexcomp\session.json"))!;
        var marks = session["bookmarks"]!.AsArray();
        Assert.Equal("entry", (string?)marks.Single(m => (int)m!["line"]! == 4)!["name"]);
        Assert.False(marks.Single(m => (int)m!["line"]! == 6)!.AsObject().ContainsKey("name"));
        Assert.True(await _services.Projects.CloseAsync());
        var again = TestComposition.Build(o => o.FileSystem = _fs);
        Assert.True(await again.Projects.OpenAsync(Dir + @"\Hello.pproj"));
        Assert.Equal([(4, (int?)3, (string?)"entry"), (6, null, null)], again.Bookmarks.All.Select(b => (b.Line, b.Number, b.Name)));
        Assert.True(await again.Projects.CloseAsync());

        // A session written before names existed loads unchanged.
        _fs.WriteAllBytes(Dir + @"\.freexcomp\session.json", Encoding.UTF8.GetBytes("""
            { "formatVersion": 1, "documents": [], "bookmarks": [ { "path": "A.pas", "line": 2, "number": 1 }, { "path": "B.pas", "line": 5, "number": null } ] }
            """));
        var old = TestComposition.Build(o => o.FileSystem = _fs);
        Assert.True(await old.Projects.OpenAsync(Dir + @"\Hello.pproj"));
        Assert.Equal([(Dir + @"\A.pas", 2, (int?)1, (string?)null), (Dir + @"\B.pas", 5, null, null)], old.Bookmarks.All.Select(b => (b.Path, b.Line, b.Number, b.Name)));

        // IDE-ED-116: toggling a named bookmark off removes it with its name.
        var reopened = Assert.IsType<TextDocumentViewModel>((await old.Documents.OpenAsync(Dir + @"\A.pas")).Value);
        old.FocusTracker.Update(new FocusState(CommandScope.Editor, null, reopened, false));
        Assert.True(old.Bookmarks.Rename(Assert.Single(old.Bookmarks.All, b => b.Line == 2), "first"));
        reopened.ViewState.Caret = new TextPosition(2, 1);
        await old.Commands.ExecuteAsync("search.bookmark.toggle1");
        Assert.DoesNotContain(old.Bookmarks.All, b => b.Line == 2);
        await old.Commands.ExecuteAsync("search.bookmark.toggle");
        Assert.Null(Assert.Single(old.Bookmarks.All, b => b.Line == 2).Name);
    }

    /// <summary>
    /// IDE-ED-119 (I-315): <c>Changed</c> on add, rename, renumber and moves; the preview text comes from the buffer while
    /// the document is open (trimmed, at most 200 characters) and from the file on disk, read in the background, when it
    /// is closed or not open yet; an unreadable file gives an empty preview.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-ED-119")]
    public Task PreviewFollowsEditsAndClosedFiles() => TestUiThread.RunAsync(PreviewFollowsEditsAndClosedFilesOnUiThread);

    private async Task PreviewFollowsEditsAndClosedFilesOnUiThread()
    {
        var bookmarks = _services.Bookmarks;
        Assert.True(await _services.Projects.OpenAsync(Dir + @"\Hello.pproj"));
        var changes = 0;
        bookmarks.Changed += (_, _) => changes++;

        // Bookmarks of documents that are not open: read from disk in the background.
        _fs.WriteAllBytes(Dir + @"\B.pas", Encoding.UTF8.GetBytes("one\r\n   two  \r\nthree\nfour\rfive\r\n"));
        bookmarks.Load([new BookmarkInfo(Dir + @"\B.pas", 2, null), new BookmarkInfo(Dir + @"\B.pas", 5, 4), new BookmarkInfo(Dir + @"\Missing.pas", 1, null)]);
        await bookmarks.Pending;
        Assert.Equal(["two", "five", ""], bookmarks.All.Select(b => b.Text));
        Assert.True(changes >= 2); // the load, then the previews

        // Open: from the buffer, following edits; Changed on add, rename, renumber and move.
        var a = await OpenAsync("A.pas");
        changes = 0;
        var added = bookmarks.Ensure(a, 2);
        Assert.Equal((1, "line 2"), (changes, added.Text));
        Assert.True(bookmarks.Rename(added, "two"));
        Assert.Equal(2, changes);
        await RunAsync("search.bookmark.toggle5", a, 2);
        Assert.Equal(3, changes);
        a.Buffer.ApplyEdits([new TextEdit(0, 0, "new\r\n")]); // moves it to line 3
        Assert.Equal(4, changes);
        Assert.Equal((3, "line 2"), (Single(3).Line, Single(3).Text));
        var (start, length, _) = a.Buffer.GetLine(3);
        a.Buffer.ApplyEdits([new TextEdit(start, length, "\t  " + new string('y', 250) + "  ")]);
        Assert.Equal(new string('y', 200), Single(3).Text); // trimmed, at most 200
        a.Buffer.ApplyEdits([new TextEdit(start, 255, "  begin  ")]);
        Assert.Equal("begin", Single(3).Text);

        // Closed: the file on disk, read in the background, then Changed.
        Assert.True(await _services.Documents.SaveAsync(a));
        _fs.WriteAllBytes(Dir + @"\A.pas", Encoding.UTF8.GetBytes("a\r\nb\r\n  on disk\r\n"));
        changes = 0;
        await _services.Documents.CloseAsync(a);
        await bookmarks.Pending;
        Assert.Equal("on disk", Single(3).Text);
        Assert.True(changes >= 1);

        // An unreadable file: an empty preview.
        _fs.WriteAllBytes(Dir + @"\Binary.pas", [0x41, 0x00, 0x42]);
        bookmarks.Load([new BookmarkInfo(Dir + @"\Binary.pas", 1, null), new BookmarkInfo(Dir + @"\B.pas", 9, null)]);
        await bookmarks.Pending;
        Assert.Equal(["", ""], bookmarks.All.Select(b => b.Text)); // binary; a line beyond the end
    }
}
