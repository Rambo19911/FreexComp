using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The New Project dialog (dialogs.md §3, UI-DLG-030…033) driven through <c>file.newProject</c>.</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class NewProjectDialogTests
{
    private static void SetText(DialogDriver dlg, string field, string text) => dlg.Find<TextBox>(field).Text = text;

    /// <summary>Waits until the debounced background check (UI-DLG-030) has run for the current values.</summary>
    private static Task CheckedAsync(HeadlessHarness h, DialogDriver dlg) =>
        h.WaitForAsync(() => !((Core.Dialogs.NewProjectViewModel)dlg.Window.Model).IsChecking, "New Project check done");

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-006")]
    [Trait("Req", "UI-DLG-030")]
    [Trait("Req", "UI-DLG-031")]
    [Trait("Req", "UI-DLG-032")]
    [Trait("Req", "UI-DLG-033")]
    [Trait("Req", "IDE-PM-021")]
    [Trait("Req", "IDE-PM-022")]
    public async Task ValidationMessages()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles[Path.Combine("Busy", "x.txt")] = "x");
        h.Services.Settings.Set(SettingsCatalog.EnvironmentDefaultProjectsDir, h.TempDir);

        var running = h.Services.Commands.ExecuteAsync("file.newProject");
        var dlg = await h.WaitForDialogAsync("NewProject");
        var ok = dlg.Find<Button>("Ok");
        Assert.Equal("New Project", dlg.Window.Title);
        Assert.Equal((640.0, 400.0, true), (dlg.Window.Width, dlg.Window.Height, dlg.Window.CanResize));

        // Templates: console-pascal first and selected; the description pane shows name, description and language.
        var templates = dlg.Find<ListBox>("Templates");
        var item = Assert.IsType<ListBoxItem>(templates.ContainerFromIndex(0));
        Assert.Equal("Dialog.NewProject.Templates.console-pascal", AutomationIds.Get(item));
        Assert.Equal("Console Application", AutomationProperties.GetName(item));
        Assert.True(item.IsSelected);
        var description = dlg.Find<Control>("TemplateDescription");
        Assert.Equal(
            ["Console Application", "A program that reads and writes text in a console window.", "Language: Object Pascal"],
            description.GetLogicalDescendantsTexts());

        // Initial focus on Name with its text selected; Tab order Templates → Name → Location → Browse → Create → Cancel.
        var name = dlg.Find<TextBox>("Name");
        Assert.Equal("Dialog.NewProject.Name", dlg.FocusedAutomationId);
        Assert.Equal("Project1", name.SelectedText);
        Assert.Equal(64, name.MaxLength);
        dlg.Press("Tab");
        Assert.Equal("Dialog.NewProject.Location", dlg.FocusedAutomationId);
        dlg.Press("Tab");
        Assert.Equal("Dialog.NewProject.Location.Browse", dlg.FocusedAutomationId);
        dlg.Press("Shift+Tab");
        dlg.Press("Shift+Tab");
        dlg.Press("Shift+Tab");
        Assert.StartsWith("Dialog.NewProject.Templates", dlg.FocusedAutomationId, StringComparison.Ordinal);

        // Label mnemonics (UI-DLG-004): Alt+L → Location, Alt+N → Name, Alt+T → Templates.
        dlg.Press("Alt+L");
        Assert.Equal("Dialog.NewProject.Location", dlg.FocusedAutomationId);
        dlg.Press("Alt+N");
        Assert.Equal("Dialog.NewProject.Name", dlg.FocusedAutomationId);
        dlg.Press("Alt+T");
        Assert.StartsWith("Dialog.NewProject.Templates", dlg.FocusedAutomationId, StringComparison.Ordinal);

        await CheckedAsync(h, dlg);
        Assert.True(ok.IsEnabled);
        Assert.Equal(Path.Combine(h.TempDir, "Project1"), dlg.Find<TextBlock>("TargetPath").Text);
        Assert.Null(dlg.ErrorText("Name"));
        Assert.Null(dlg.ErrorText("Location"));

        // Typing: the default button is disabled while the check is pending, the message follows after 200 ms.
        dlg.Type("Name", " x");
        Assert.False(ok.IsEnabled);
        Assert.Equal(Path.Combine(h.TempDir, "Project1 x"), dlg.Find<TextBlock>("TargetPath").Text); // live
        await CheckedAsync(h, dlg);
        Assert.Equal("A project name starts with a letter or underscore and contains only letters, digits and underscores.", dlg.ErrorText("Name"));
        Assert.Contains("invalid", name.Classes);
        Assert.False(ok.IsEnabled);
        dlg.Press("Enter"); // nothing happens while invalid
        Assert.True(dlg.Window.IsVisible);

        SetText(dlg, "Name", "");
        await CheckedAsync(h, dlg);
        Assert.Equal("Enter a project name.", dlg.ErrorText("Name"));
        SetText(dlg, "Name", "begin");
        await CheckedAsync(h, dlg);
        Assert.Equal("begin is a reserved word and cannot be used as a project name.", dlg.ErrorText("Name"));
        SetText(dlg, "Name", "System");
        await CheckedAsync(h, dlg);
        Assert.Equal("System is a reserved word and cannot be used as a project name.", dlg.ErrorText("Name"));

        SetText(dlg, "Name", "Busy");
        await CheckedAsync(h, dlg);
        Assert.Null(dlg.ErrorText("Name"));
        Assert.DoesNotContain("invalid", name.Classes);
        Assert.Equal($"The folder {Path.Combine(h.TempDir, "Busy")} already exists and is not empty.", dlg.ErrorText("Location"));
        Assert.False(ok.IsEnabled);

        SetText(dlg, "Location", "");
        await CheckedAsync(h, dlg);
        Assert.Equal("Choose a folder for the project.", dlg.ErrorText("Location"));
        Assert.Contains("invalid", dlg.Find<TextBox>("Location").Classes);
        SetText(dlg, "Location", @"relative\folder");
        await CheckedAsync(h, dlg);
        Assert.Equal("The folder path is not valid.", dlg.ErrorText("Location"));
        SetText(dlg, "Location", @"C:\" + new string('d', 230)); // C:\ddd…\Busy\Busy.pproj = 249 characters
        await CheckedAsync(h, dlg);
        Assert.Equal("The resulting path is longer than 240 characters.", dlg.ErrorText("Location"));

        // A failed creation shows Msg.Project.CreateFailed and reopens the dialog with the same values.
        var location = Path.Combine(h.TempDir, "New");
        SetText(dlg, "Location", location.Replace('\\', '/'));
        SetText(dlg, "Name", "Hello");
        await CheckedAsync(h, dlg);
        Assert.True(ok.IsEnabled);
        Assert.Equal(Path.Combine(location, "Hello"), dlg.Find<TextBlock>("TargetPath").Text);
        h.FileSystem.FailWhen = call => call.StartsWith("WriteAllBytesDurable", StringComparison.Ordinal) && call.Contains(".pproj", StringComparison.Ordinal);
        h.Messages.Answer("Msg.Project.CreateFailed", "OK");
        dlg.Press("Enter");
        await h.WaitForAsync(() => h.Messages.Requests.Count == 1, "CreateFailed shown");
        dlg = await h.WaitForDialogAsync("NewProject");
        Assert.Equal("Hello", dlg.Find<TextBox>("Name").Text);
        Assert.Equal(location, dlg.Find<TextBox>("Location").Text);
        Assert.False(Directory.Exists(location));

        // Create (Enter) creates and opens the project; the location is remembered.
        h.FileSystem.FailWhen = null;
        await CheckedAsync(h, dlg);
        dlg.Press("Enter");
        await running;
        Assert.True(File.Exists(Path.Combine(location, "Hello", "Hello.pproj")));
        Assert.Equal("Hello", h.Services.Projects.Project?.Name);
        Assert.Equal(Path.Combine(location, "Hello", "Hello.pas"), h.Services.Documents.ActiveDocument?.FilePath);
        Assert.Equal(location, h.Services.Layout.GetDialogValue("NewProject", "lastLocation"));

        // Cancel (Escape) closes without changes.
        running = h.Services.Commands.ExecuteAsync("file.newProject");
        dlg = await h.WaitForDialogAsync("NewProject");
        dlg.Press("Escape");
        await running;
        Assert.Equal("Hello", h.Services.Projects.Project?.Name);
        Assert.Single(h.Services.Documents.Documents);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-011")]
    [Trait("Req", "UI-DLG-031")]
    public async Task DefaultNameSkipsExistingFolders()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles[Path.Combine("Projects", "Project1", "a.txt")] = "a";
            o.InitialFiles[Path.Combine("Projects", "Project2", "b.txt")] = "b";
            o.InitialFiles[Path.Combine("Projects", "Project3")] = "a file counts too";
            o.InitialFiles[Path.Combine("Other", "Project1", "c.txt")] = "c";
        });
        var projects = Path.Combine(h.TempDir, "Projects");
        h.Services.Settings.Set(SettingsCatalog.EnvironmentDefaultProjectsDir, projects);

        // Location = environment.defaultProjectsDir; name = the first Project<n> that does not exist there.
        var running = h.Services.Commands.ExecuteAsync("file.newProject");
        var dlg = await h.WaitForDialogAsync("NewProject");
        Assert.Equal(projects, dlg.Find<TextBox>("Location").Text);
        Assert.Equal("Project4", dlg.Find<TextBox>("Name").Text);

        // Browse opens the folder picker at the field's value; the picked folder replaces it.
        var other = Path.Combine(h.TempDir, "Other");
        h.FileDialogs.NextFolder(other);
        dlg.Click("Location.Browse");
        await h.WaitForAsync(() => dlg.Find<TextBox>("Location").Text == other, "picked folder shown");
        Assert.Equal(projects, h.FileDialogs.Requests[^1].InitialFolder);
        Assert.Equal("Project4", dlg.Find<TextBox>("Name").Text); // the name stays as it is
        await CheckedAsync(h, dlg);
        dlg.Click("Ok"); // focus is on Browse, which would take Enter itself
        await running;
        Assert.Equal("Project4", h.Services.Projects.Project?.Name);
        Assert.True(File.Exists(Path.Combine(other, "Project4", "Project4.pas")));

        // Next time: the last location used in this dialog, and the name skips Project1 and the new Project4 there.
        running = h.Services.Commands.ExecuteAsync("file.newProject");
        dlg = await h.WaitForDialogAsync("NewProject");
        Assert.Equal(other, dlg.Find<TextBox>("Location").Text);
        Assert.Equal("Project2", dlg.Find<TextBox>("Name").Text);
        dlg.Press("Escape");
        await running;
    }

    /// <summary>
    /// I-255: a mouse click on <c>Create</c> while a text field has the focus creates the project. The press moves the
    /// focus out of the field; leaving a field must not disable the button under the pointer.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-006")]
    [Trait("Req", "UI-DLG-030")]
    public async Task ClickOnCreate_CreatesProject()
    {
        await using var h = await HeadlessHarness.StartAsync();
        h.Services.Settings.Set(SettingsCatalog.EnvironmentDefaultProjectsDir, h.TempDir);

        // Right after the dialog opened: the focus is in Name.
        var running = h.Services.Commands.ExecuteAsync("file.newProject");
        var dlg = await h.WaitForDialogAsync("NewProject");
        try
        {
            await CheckedAsync(h, dlg);
            Assert.Equal("Dialog.NewProject.Name", dlg.FocusedAutomationId);
            dlg.Click("Ok");
            Assert.True(dlg.Window.Model.IsClosed, "the click on Create closed the dialog");
            await running;
            Assert.True(File.Exists(Path.Combine(h.TempDir, "Project1", "Project1.pproj")));
            Assert.Equal("Project1", h.Services.Projects.Project?.Name);

            // With a typed location and the focus in Location.
            var typed = Path.Combine(h.TempDir, "Typed");
            running = h.Services.Commands.ExecuteAsync("file.newProject");
            dlg = await h.WaitForDialogAsync("NewProject");
            SetText(dlg, "Location", "");
            dlg.Type("Location", typed);
            await CheckedAsync(h, dlg);
            Assert.Equal("Dialog.NewProject.Location", dlg.FocusedAutomationId);
            dlg.Click("Ok");
            Assert.True(dlg.Window.Model.IsClosed, "the click on Create closed the dialog");
            await running;
            Assert.True(File.Exists(Path.Combine(typed, "Project2", "Project2.pproj"))); // Project1 exists at the first location

            // Leaving an empty field still shows its message (UI-DLG-006) and Create stays disabled.
            running = h.Services.Commands.ExecuteAsync("file.newProject");
            dlg = await h.WaitForDialogAsync("NewProject");
            await CheckedAsync(h, dlg);
            dlg.Press("Tab"); // Name → Location
            SetText(dlg, "Location", "");
            await CheckedAsync(h, dlg);
            dlg.Press("Tab"); // leaves Location
            await CheckedAsync(h, dlg);
            Assert.Equal("Choose a folder for the project.", dlg.ErrorText("Location"));
            Assert.False(dlg.Find<Button>("Ok").IsEnabled);
        }
        finally
        {
            await CancelAsync(dlg, running);
        }
    }

    /// <summary>Closes a dialog that is still open, so that a failed assertion is reported and not the open dialog.</summary>
    private static async Task CancelAsync(DialogDriver dlg, Task running)
    {
        if (!dlg.Window.Model.IsClosed)
            dlg.Press("Escape");
        await running;
    }

    /// <summary>I-256 (a): the 26 × 26 <c>…</c> button (UI-DLG-011) shows its glyph — the text is not clipped away by padding.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-011")]
    public async Task BrowseButtonHasGlyph()
    {
        await using var h = await HeadlessHarness.StartAsync();
        h.Services.Settings.Set(SettingsCatalog.EnvironmentDefaultProjectsDir, h.TempDir);
        var running = h.Services.Commands.ExecuteAsync("file.newProject");
        var dlg = await h.WaitForDialogAsync("NewProject");

        try
        {
            var browse = dlg.Find<Button>("Location.Browse");
            Assert.Equal((26.0, 26.0), (browse.Bounds.Width, browse.Bounds.Height));
            AssertGlyphFits(browse);
        }
        finally
        {
            await CancelAsync(dlg, running);
        }

        // The New File dialog has the same button beside Folder.
        running = h.Services.Commands.ExecuteAsync("file.newFile");
        dlg = await h.WaitForDialogAsync("NewFile");
        try
        {
            AssertGlyphFits(dlg.Find<Button>("Folder.Browse"));
        }
        finally
        {
            await CancelAsync(dlg, running);
        }
    }

    /// <summary>The text of <paramref name="button"/> is laid out at its natural size, inside the button.</summary>
    internal static void AssertGlyphFits(Button button)
    {
        var text = Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(button).OfType<TextBlock>());
        Assert.Equal("…", text.Text);
        var natural = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight };
        natural.Measure(Avalonia.Size.Infinity);
        Assert.True(natural.DesiredSize.Width > 0 && natural.DesiredSize.Height > 0, "the glyph has a size");
        Assert.True(text.Bounds.Width >= natural.DesiredSize.Width && text.Bounds.Height >= natural.DesiredSize.Height,
            $"the glyph needs {natural.DesiredSize} but gets {text.Bounds.Size}");
        var inButton = Avalonia.VisualExtensions.TranslatePoint(text, default, button)!.Value;
        Assert.True(inButton.X >= 0 && inButton.Y >= 0
            && inButton.X + text.Bounds.Width <= button.Bounds.Width && inButton.Y + text.Bounds.Height <= button.Bounds.Height,
            $"the glyph at {inButton} size {text.Bounds.Size} lies outside the button");
    }

    /// <summary>I-256 (b): the path follows its label <c>Will be created in:</c> after the same gap as the other fields.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-030")]
    public async Task TargetPathLabelHasSpace()
    {
        await using var h = await HeadlessHarness.StartAsync();
        h.Services.Settings.Set(SettingsCatalog.EnvironmentDefaultProjectsDir, h.TempDir);
        var running = h.Services.Commands.ExecuteAsync("file.newProject");
        var dlg = await h.WaitForDialogAsync("NewProject");

        try
        {
            var target = dlg.Find<TextBlock>("TargetPath");
            var label = Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dlg.Window).OfType<TextBlock>(),
                t => t.Text == "Will be created in:");
            var labelRight = Avalonia.VisualExtensions.TranslatePoint(label, new Avalonia.Point(label.Bounds.Width, 0), dlg.Window)!.Value.X;
            var targetLeft = Avalonia.VisualExtensions.TranslatePoint(target, default, dlg.Window)!.Value.X;
            Assert.True(targetLeft - labelRight >= 16, $"gap between the label and the path is {targetLeft - labelRight}");
            // The path starts where the Name and Location fields start.
            var nameLeft = Avalonia.VisualExtensions.TranslatePoint(dlg.Find<TextBox>("Name"), default, dlg.Window)!.Value.X;
            Assert.Equal(nameLeft, targetLeft);
        }
        finally
        {
            await CancelAsync(dlg, running);
        }
    }
}

internal static class ControlTextExtensions
{

    /// <summary>The non-empty texts of the <see cref="TextBlock"/>s under <paramref name="control"/>, in tree order.</summary>
    public static IReadOnlyList<string> GetLogicalDescendantsTexts(this Control control) =>
        [.. Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(control).OfType<TextBlock>()
            .Select(t => t.Text ?? "").Where(t => t.Length > 0)];
}
