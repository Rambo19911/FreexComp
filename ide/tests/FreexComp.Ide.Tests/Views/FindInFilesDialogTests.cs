using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Find in Files dialog (I-123, dialogs.md §11).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class FindInFilesDialogTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-110")]
    [Trait("Req", "UI-DLG-111")]
    public async Task ScopeValidation()
    {
        await using var h = await BuildScenario.StartAsync();
        Assert.True((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Success);
        var editor = h.Find<TextEditor>("Editor.Unit1.pas");
        editor.TextArea.Focus();
        editor.TextArea.Caret.Offset = editor.Document.Text.IndexOf("Foo1", StringComparison.Ordinal) + 1;
        Dispatcher.UIThread.RunJobs();

        // Defaults: the word at the caret, scope Project, folder fields disabled.
        var opening = h.Services.Commands.ExecuteAsync("search.findInFiles");
        var dlg = await h.WaitForDialogAsync("FindInFiles");
        Assert.Equal((Strings.FindInFiles_Title, 560.0, false), (dlg.Window.Title, dlg.Window.Width, dlg.Window.CanResize));
        Assert.Equal("Foo1", dlg.Find<TextBox>("SearchText").Text);
        Assert.True(dlg.Find<RadioButton>("Scope.Project").IsChecked);
        Assert.False(dlg.Find<TextBox>("Folder").IsEffectivelyEnabled);
        Assert.False(dlg.Find<CheckBox>("Subfolders").IsEffectivelyEnabled);
        Assert.Equal("*.pas;*.inc;*.form", dlg.Find<TextBox>("FileTypes").Text);

        // Empty text and an invalid pattern block Find.
        dlg.Find<TextBox>("SearchText").Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Strings.Msg_FindInFiles_TextEmpty, dlg.ErrorText("SearchText"));
        Assert.False(dlg.Find<Button>("Find").IsEffectivelyEnabled);
        dlg.Find<TextBox>("SearchText").Text = "(Foo";
        dlg.Find<CheckBox>("Regex").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith(Strings.Msg_Find_RegexInvalid.Split('{')[0], dlg.ErrorText("SearchText"), StringComparison.Ordinal);
        Assert.False(dlg.Find<Button>("Find").IsEffectivelyEnabled);
        dlg.Find<CheckBox>("Regex").IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(dlg.ErrorText("SearchText"));

        // Folder scope: enables its fields; a missing folder blocks, an existing one does not.
        dlg.Find<RadioButton>("Scope.Folder").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(dlg.Find<TextBox>("Folder").IsEffectivelyEnabled);
        Assert.Equal(Path.GetDirectoryName(BuildScenario.ProjectPath(h)), dlg.Find<TextBox>("Folder").Text);
        dlg.Find<TextBox>("Folder").Text = Path.Combine(h.TempDir, "missing");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Strings.Msg_FindInFiles_FolderMissing, dlg.ErrorText("Folder"));
        Assert.False(dlg.Find<Button>("Find").IsEffectivelyEnabled);
        dlg.Find<TextBox>("Folder").Text = h.TempDir;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(dlg.ErrorText("Folder"));
        dlg.Find<RadioButton>("Scope.Project").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        // Cancel: closes, option changes are not remembered.
        dlg.Find<CheckBox>("MatchCase").IsChecked = true;
        dlg.Press("Escape");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog canceled");
        await opening;
        Assert.Null(h.TryFind<Control>("Tool.SearchResults.List"));

        // Find: closes, shows Search Results and remembers the options.
        opening = h.Services.Commands.ExecuteAsync("search.findInFiles");
        dlg = await h.WaitForDialogAsync("FindInFiles");
        Assert.False(dlg.Find<CheckBox>("MatchCase").IsChecked);
        dlg.Find<CheckBox>("MatchCase").IsChecked = true;
        dlg.Click("Find");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        await opening;
        await h.WaitForAsync(() => h.TryFind<TextBlock>("Tool.SearchResults.Header")?.Text?.Contains("(match case)", StringComparison.Ordinal) == true,
            "search results shown");
        Assert.Contains("— 1 match in 1 file", h.Find<TextBlock>("Tool.SearchResults.Header").Text, StringComparison.Ordinal);
        opening = h.Services.Commands.ExecuteAsync("search.findInFiles");
        dlg = await h.WaitForDialogAsync("FindInFiles");
        Assert.True(dlg.Find<CheckBox>("MatchCase").IsChecked);
        dlg.Press("Escape");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog canceled");
        await opening;

        // No project: Project is disabled and Open files is the default.
        Assert.True(await h.Services.Projects.CloseAsync());
        opening = h.Services.Commands.ExecuteAsync("search.findInFiles");
        dlg = await h.WaitForDialogAsync("FindInFiles");
        Assert.False(dlg.Find<RadioButton>("Scope.Project").IsEffectivelyEnabled);
        Assert.True(dlg.Find<RadioButton>("Scope.OpenFiles").IsChecked);
        dlg.Press("Escape");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog canceled");
        await opening;
    }
}
