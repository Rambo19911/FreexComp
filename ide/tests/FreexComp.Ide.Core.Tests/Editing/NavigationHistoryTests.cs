using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Core.Editing;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>Navigation history, back and forward (I-060, editor-features.md IDE-ED-100, IDE-ED-072).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Unit")]
public sealed class NavigationHistoryTests
{
    private const string Back = "search.navigateBack";
    private const string Forward = "search.navigateForward";

    /// <summary>60 lines: <c>begin</c> on line 2 pairs with <c>end</c> on line 17, <c>begin</c> 18 with <c>end</c> 20.</summary>
    private static string CodeA() =>
        "program A;\r\nbegin\r\n" + string.Concat(Enumerable.Repeat("  X;\r\n", 14)) + "end;\r\nbegin\r\n  Y;\r\nend;\r\n" +
        string.Concat(Enumerable.Range(21, 40).Select(n => $"// line {n}\r\n"));

    private static string Lines(int count) => string.Concat(Enumerable.Range(1, count).Select(n => $"// {n}\r\n"));

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-100")]
    [Trait("Req", "IDE-ED-072")]
    public Task BackForwardAndMerging() => TestUiThread.RunAsync(() => BackForwardAndMergingOnUiThread()); // I-209

    private async Task BackForwardAndMergingOnUiThread()
    {
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        fs.CreateDirectory(@"C:\t");
        fs.WriteAllBytes(@"C:\t\A.pas", Encoding.UTF8.GetBytes(CodeA()));
        fs.WriteAllBytes(@"C:\t\B.pas", Encoding.UTF8.GetBytes(Lines(30)));
        fs.WriteAllBytes(@"C:\t\C.pas", Encoding.UTF8.GetBytes(Lines(8)));
        var navigation = services.Navigation;
        var commands = services.Commands;

        // Opening the first document leaves no location behind: nothing to go back to.
        var a = await OpenAsync(services, @"C:\t\A.pas");
        Assert.Empty(Entries(navigation));
        Assert.False(commands.CanExecute(Back));
        Assert.False(commands.CanExecute(Forward));

        // A bracket jump of more than 10 lines records its departure; a shorter one does not (IDE-ED-072).
        a.Selection = TextSelection.At(a.Buffer.GetOffset(2, 1));
        await commands.ExecuteAsync("search.goToMatchingBracket");
        Assert.Equal(new TextPosition(17, 1), a.ViewState.Caret);
        a.Selection = TextSelection.At(a.Buffer.GetOffset(18, 1));
        await commands.ExecuteAsync("search.goToMatchingBracket");
        Assert.Equal(new TextPosition(20, 1), a.ViewState.Caret);
        Assert.Equal(["A.pas 2:1"], Entries(navigation));
        Assert.True(commands.CanExecute(Back));

        // Go to Line records the departure (the Navigated event); consecutive entries of one document within 10 lines
        // are merged, the newer replacing the older.
        navigation.GoToLine(a, 40, 1);
        navigation.GoToLine(a, 45, 3);
        Assert.Equal(["A.pas 2:1", "A.pas 20:1", "A.pas 40:1"], Entries(navigation));
        navigation.GoToLine(a, 58, 1);
        Assert.Equal(["A.pas 2:1", "A.pas 20:1", "A.pas 45:3"], Entries(navigation));

        // Opening (switching to) another document records the location it left.
        var b = await OpenAsync(services, @"C:\t\B.pas");
        navigation.GoToLine(b, 20, 4);
        Assert.Equal(["A.pas 2:1", "A.pas 20:1", "A.pas 45:3", "A.pas 58:1", "B.pas 1:1"], Entries(navigation));
        Assert.Equal(5, navigation.HistoryIndex); // at the live end
        Assert.False(commands.CanExecute(Forward));

        // Back first records the current location, then moves to the previous entry, across documents.
        await commands.ExecuteAsync(Back);
        Assert.Equal(["A.pas 2:1", "A.pas 20:1", "A.pas 45:3", "A.pas 58:1", "B.pas 1:1", "B.pas 20:4"], Entries(navigation));
        Assert.Equal((b, new TextPosition(1, 1), 4), (services.Documents.ActiveDocument, b.ViewState.Caret, navigation.HistoryIndex));
        await commands.ExecuteAsync(Back);
        Assert.Equal((a, new TextPosition(58, 1)), (services.Documents.ActiveDocument, a.ViewState.Caret));
        Assert.Equal(6, Entries(navigation).Count); // moving through the history records nothing
        Assert.True(commands.CanExecute(Forward));

        // Entries are anchored: they move with edits.
        a.Buffer.ApplyEdits([new TextEdit(0, 0, "\r\n\r\n\r\n")]);
        Assert.Equal(["A.pas 5:1", "A.pas 23:1", "A.pas 48:3", "A.pas 61:1", "B.pas 1:1", "B.pas 20:4"], Entries(navigation));
        await commands.ExecuteAsync(Back);
        Assert.Equal(new TextPosition(48, 3), a.ViewState.Caret);
        await commands.ExecuteAsync(Back);
        await commands.ExecuteAsync(Back);
        Assert.Equal((new TextPosition(5, 1), 0), (a.ViewState.Caret, navigation.HistoryIndex));
        Assert.False(commands.CanExecute(Back)); // menus.md: enabled when there is an earlier entry

        // Forward walks the same way back to the end.
        await commands.ExecuteAsync(Forward);
        await commands.ExecuteAsync(Forward);
        Assert.Equal(new TextPosition(48, 3), a.ViewState.Caret);
        await commands.ExecuteAsync(Forward);
        await commands.ExecuteAsync(Forward);
        Assert.Equal((b, new TextPosition(1, 1)), (services.Documents.ActiveDocument, b.ViewState.Caret));
        await commands.ExecuteAsync(Forward);
        Assert.Equal((new TextPosition(20, 4), 5), (b.ViewState.Caret, navigation.HistoryIndex));
        Assert.False(commands.CanExecute(Forward));

        // An entry in a closed document reopens it at the entry's position.
        Assert.True(services.Documents.Close(b)); // closing the active document records nothing
        Assert.Equal(6, Entries(navigation).Count);
        await commands.ExecuteAsync(Back);
        var reopened = Assert.IsType<TextDocumentViewModel>(services.Documents.ActiveDocument);
        Assert.Equal((@"C:\t\B.pas", new TextPosition(1, 1)), (reopened.FilePath, reopened.ViewState.Caret));
        await commands.ExecuteAsync(Forward);
        Assert.Equal(new TextPosition(20, 4), reopened.ViewState.Caret);

        // A new jump in the middle of the history drops the entries after the current one.
        await commands.ExecuteAsync(Back);
        await commands.ExecuteAsync(Back);
        Assert.Equal((a, 3), (services.Documents.ActiveDocument, navigation.HistoryIndex));
        navigation.GoToLine(a, 30, 1);
        Assert.Equal(["A.pas 5:1", "A.pas 23:1", "A.pas 48:3", "A.pas 61:1"], Entries(navigation));
        Assert.False(commands.CanExecute(Forward));

        // Entries whose file no longer exists are skipped and removed.
        var c = await OpenAsync(services, @"C:\t\C.pas"); // records A 30:1
        navigation.GoToLine(c, 8, 1);
        services.Documents.Activate(reopened); // records C 8:1, merged with C 1:1
        Assert.Equal(["A.pas 5:1", "A.pas 23:1", "A.pas 48:3", "A.pas 61:1", "A.pas 30:1", "C.pas 8:1"], Entries(navigation));
        Assert.True(services.Documents.Close(c));
        fs.DeleteFile(@"C:\t\C.pas");
        await commands.ExecuteAsync(Back); // records B 1:1 (where Back left B's caret), skips and removes C
        Assert.Equal(["A.pas 5:1", "A.pas 23:1", "A.pas 48:3", "A.pas 61:1", "A.pas 30:1", "B.pas 1:1"], Entries(navigation));
        Assert.Equal((a, new TextPosition(30, 1), 4), (services.Documents.ActiveDocument, a.ViewState.Caret, navigation.HistoryIndex));

        // Nothing reachable in that direction any more: Msg.Nav.NoForward.
        Assert.True(services.Documents.Close(reopened));
        fs.DeleteFile(@"C:\t\B.pas");
        Assert.True(commands.CanExecute(Forward));
        await commands.ExecuteAsync(Forward);
        Assert.Equal(["A.pas 5:1", "A.pas 23:1", "A.pas 48:3", "A.pas 61:1", "A.pas 30:1"], Entries(navigation));
        Assert.Equal((Strings.Msg_Nav_NoForward, a, new TextPosition(30, 1)),
            (services.Status.Message, services.Documents.ActiveDocument, a.ViewState.Caret));
        Assert.False(commands.CanExecute(Forward));

        // At most 50 entries: the oldest go.
        for (var i = 0; i < 60; i++)
            navigation.GoToLine(a, i % 2 == 0 ? 40 : 1, 1);
        Assert.Equal(50, Entries(navigation).Count);
        Assert.Equal(50, navigation.HistoryIndex);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-100")]
    public Task BackFromTheOnlyNearbyEntryReportsNoBack() => TestUiThread.RunAsync(() => BackFromTheOnlyNearbyEntryReportsNoBackOnUiThread()); // I-209

    private async Task BackFromTheOnlyNearbyEntryReportsNoBackOnUiThread()
    {
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        fs.CreateDirectory(@"C:\t");
        fs.WriteAllBytes(@"C:\t\D.pas", Encoding.UTF8.GetBytes(Lines(20)));
        var d = await OpenAsync(services, @"C:\t\D.pas");

        // The current location recorded by Back merges with the only entry (within 10 lines): nothing earlier.
        services.Navigation.GoToLine(d, 5, 1);
        Assert.True(services.Commands.CanExecute(Back));
        await services.Commands.ExecuteAsync(Back);
        Assert.Equal(["D.pas 5:1"], Entries(services.Navigation));
        Assert.Equal((Strings.Msg_Nav_NoBack, new TextPosition(5, 1)), (services.Status.Message, d.ViewState.Caret));
        Assert.False(services.Commands.CanExecute(Back));

        // Find: a wrap-around always records its departure, a jump of more than 10 lines too, a shorter one not.
        d.Selection = TextSelection.At(d.Buffer.GetOffset(18, 1));
        services.Find.SearchText = "// 3";
        await services.Commands.ExecuteAsync("search.findNext");
        Assert.Equal(new TextPosition(3, 5), d.ViewState.Caret);
        services.Find.SearchText = "// 7";
        await services.Commands.ExecuteAsync("search.findNext");
        services.Find.SearchText = "// 19";
        await services.Commands.ExecuteAsync("search.findNext");
        Assert.Equal(new TextPosition(19, 6), d.ViewState.Caret);
        // (Back left the user at the only entry, so the first new departure replaced it.)
        Assert.Equal(["D.pas 18:1", "D.pas 7:5"], Entries(services.Navigation));
    }

    private static async Task<TextDocumentViewModel> OpenAsync(IdeServices services, string path)
    {
        var document = Assert.IsType<TextDocumentViewModel>((await services.Documents.OpenAsync(path)).Value);
        services.FocusTracker.Update(new FocusState(CommandScope.Editor, null, document, false));
        return document;
    }

    private static List<string> Entries(INavigationService navigation) =>
        [.. navigation.History.Select(e => $"{e.Name} {e.Position.Line}:{e.Position.Column}")];
}
