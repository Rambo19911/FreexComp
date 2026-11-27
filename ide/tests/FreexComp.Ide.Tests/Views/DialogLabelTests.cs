using System.Collections;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The labels and buttons of every dialog window show access keys, never the mnemonic marker itself (dialogs.md
/// "Label notation", UI-DLG-004; task I-286).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class DialogLabelTests
{
    private enum Scene
    {
        /// <summary>The Hello project with the debugger services and one open unit.</summary>
        Project,

        /// <summary>A form with a status bar, a main menu and an action list, open in the designer.</summary>
        Designer,

        /// <summary>A project group of two projects.</summary>
        Group,

        /// <summary>The Hello project with build events that are not trusted.</summary>
        Trust,
    }

    /// <summary>Starts the showing of one dialog; the task ends when the dialog is closed.</summary>
    private delegate Task Opener(HeadlessHarness h, FormDocumentViewModel? form);

    private static Opener Command(string id) => (h, _) => h.Services.Commands.ExecuteAsync(id);

    private static Opener Model<T>(Func<HeadlessHarness, FormDocumentViewModel?, DialogViewModel<T>> create) =>
        (h, form) => h.Services.Dialogs.ShowAsync(create(h, form));

    /// <summary>Dialog name (<c>Dialog.&lt;Name&gt;</c>) → its window class, the scene it needs and how it is opened.</summary>
    private static readonly Dictionary<string, (Type Window, Scene Scene, Opener Open)> Dialogs = new(StringComparer.Ordinal)
    {
        ["About"] = (typeof(AboutWindow), Scene.Project, Command("help.about")),
        ["Breakpoint"] = (typeof(BreakpointWindow), Scene.Project, Model((h, _) =>
            new BreakpointViewModel(h.Services.FileSystem, h.Services.FileDialogs, h.Services.Documents, [], BuildScenario.UnitPath(h), 1))),
        ["ChildListEditor"] = (typeof(ChildListEditorWindow), Scene.Designer, Model((h, d) =>
            new ChildListEditorViewModel(d!.Designer, d.Designer.Model!.FindByName("ActionList1")!, h.Services.Dialogs))),
        ["CollectionEditor"] = (typeof(CollectionEditorWindow), Scene.Designer, Model((h, d) =>
            new CollectionEditorViewModel(d!.Designer.Catalog, d.Designer.Model!, d.Designer.Model!.FindByName("StatusBar1")!, "Panels", h.Services.Dialogs))),
        ["ConfigurationManager"] = (typeof(ConfigurationManagerWindow), Scene.Project, Command("project.configurationManager")),
        ["RenameConfiguration"] = (typeof(RenameConfigurationWindow), Scene.Project, Model((_, _) => new RenameConfigurationViewModel(["Debug", "Release"], "Debug"))),
        ["Documents"] = (typeof(DocumentsWindow), Scene.Project, Command("window.documents")),
        ["Evaluate"] = (typeof(EvaluateWindow), Scene.Project, Model((h, _) => new EvaluateViewModel(h.Services.Debug!, h.Services.Layout, "1"))),
        ["FindInFiles"] = (typeof(FindInFilesWindow), Scene.Project, Command("search.findInFiles")),
        ["GoToLine"] = (typeof(GoToLineWindow), Scene.Project, Model((_, _) => new GoToLineViewModel(10, 1))),
        ["HelpContents"] = (typeof(HelpContentsWindow), Scene.Project, Command("help.contents")),
        ["Licenses"] = (typeof(LicensesWindow), Scene.Project, Command("help.licenses")),
        ["MenuDesigner"] = (typeof(MenuDesignerWindow), Scene.Designer, Model((h, d) =>
            new MenuDesignerViewModel(d!.Designer, d.Designer.Model!.FindByName("MainMenu1")!, h.Services.Dialogs))),
        ["Message"] = (typeof(MessageBoxWindow), Scene.Project, (h, _) => h.Services.Dialogs.ShowMessageAsync(new MessageRequest("Msg.File.NotFound", ["a.pas"]))),
        ["NewConfiguration"] = (typeof(NewConfigurationWindow), Scene.Project, Model((_, _) => new NewConfigurationViewModel(["Debug", "Release"], "Debug"))),
        ["NewFile"] = (typeof(NewFileWindow), Scene.Project, Command("file.newFile")),
        ["NewInheritedForm"] = (typeof(NewInheritedFormWindow), Scene.Project, Command("file.newInheritedForm")),
        ["NewProject"] = (typeof(NewProjectWindow), Scene.Project, Command("file.newProject")),
        ["Options"] = (typeof(OptionsWindow), Scene.Project, Command("tools.options")),
        ["ProjectDependencies"] = (typeof(ProjectDependenciesWindow), Scene.Group, Command("project.dependencies")),
        ["ProjectOptions"] = (typeof(ProjectOptionsWindow), Scene.Project, Command("project.options")),
        ["RefactoringPreview"] = (typeof(RefactoringPreviewWindow), Scene.Project, (h, _) => h.Services.Refactoring.ApplyAsync(
            new FreexComp.Ide.Core.Refactoring.WorkspaceEdit("Rename Symbol",
                [new FreexComp.Ide.Core.Refactoring.FileEdit(BuildScenario.UnitPath(h), h.Services.Documents.ActiveDocument!.VisibleText!.Buffer.Text, [new TextEdit(0, 0, "{x}")])]),
            FreexComp.Ide.Core.Refactoring.PreviewMode.Always)),
        ["RenameFile"] = (typeof(RenameFileWindow), Scene.Project, Model((_, _) =>
            new RenameFileViewModel("Unit1.pas", _ => new OperationResult(null, []), (_, _) => Task.FromResult(true)))),
        ["ReopenEncoding"] = (typeof(ReopenEncodingWindow), Scene.Project, Model((_, _) =>
            new ReopenEncodingViewModel("Unit1.pas", FileEncoding.Windows1252, FileEncoding.Utf8, isModified: false))),
        ["RunParameters"] = (typeof(RunParametersWindow), Scene.Project, Command("run.parameters")),
        ["StringsEditor"] = (typeof(StringsEditorWindow), Scene.Designer, Model((_, _) => new StringsEditorViewModel("Lines", "Memo1", ["one"]))),
        ["TextViewer"] = (typeof(TextViewerWindow), Scene.Project, Model((h, _) => new TextViewerViewModel(h.Services.Debug!, h.Services.Clipboard, h.Services.Layout, "S",
            new FreexComp.Debugger.VariableInfo("S", "'a'", "string", FreexComp.Debugger.ValueKind.String, null, 0, false, "S", null) { Text = new FreexComp.Debugger.VariableHandle(1, 1) }))),
        ["TrustProject"] = (typeof(TrustProjectWindow), Scene.Trust, (h, _) => h.Services.Build.BuildAsync(BuildKind.Build)),
        ["UnsavedChanges"] = (typeof(UnsavedChangesWindow), Scene.Project, Model((h, _) => new UnsavedChangesViewModel([.. h.Services.Documents.Documents]))),
        ["Watch"] = (typeof(WatchWindow), Scene.Project, Model((h, _) => new WatchViewModel(h.Services.Layout, "Foo1", edit: false))),
    };

    public static TheoryData<string> DialogNames() => [.. Dialogs.Keys];

    private static async Task<(HeadlessHarness Harness, FormDocumentViewModel? Form)> StartAsync(Scene scene)
    {
        switch (scene)
        {
            case Scene.Designer:
            {
                var h = await HeadlessHarness.StartAsync(DesignerViewTests.EditorFiles);
                var form = await DesignerViewTests.OpenFormAsync(h);
                h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
                return (h, form);
            }

            case Scene.Group:
            {
                var h = await HeadlessHarness.StartAsync(o =>
                {
                    foreach (var name in (string[])["App", "CoreLib"])
                    {
                        o.InitialFiles[$@"{name}\{name}.pproj"] =
                            $$"""{ "formatVersion": 1, "name": "{{name}}", "mainSource": "{{name}}.pas", "files": [ { "path": "{{name}}.pas" } ] }""";
                        o.InitialFiles[$@"{name}\{name}.pas"] = $"program {name};\nbegin\nend.\n";
                    }

                    o.InitialFiles["All.pgroup"] = """{ "formatVersion": 1, "projects": ["App/App.pproj", "CoreLib/CoreLib.pproj"], "activeProject": "App/App.pproj" }""";
                });
                Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "All.pgroup")));
                Dispatcher.UIThread.RunJobs();
                return (h, null);
            }

            case Scene.Trust:
                return (await BuildScenario.StartAsync(projectExtra: TrustDialogTests.HelloEvents), null);

            default:
            {
                var h = await DebugScenario.StartAsync();
                await DebugScenario.OpenAsync(h, DebugScenario.UnitPath(h));
                return (h, null);
            }
        }
    }

    /// <summary>
    /// What a resource text shows of its ampersands: a doubled one is one literal ampersand, a single one marks the access
    /// key and is not shown.
    /// </summary>
    private static string Shown(string resource) =>
        resource.Replace("&&", "\0", StringComparison.Ordinal).Replace("&", "", StringComparison.Ordinal).Replace('\0', '&');

    /// <summary>The texts with an ampersand that a label or button may show: those of resources that double it.</summary>
    private static readonly HashSet<string> DoubledInResources =
        [.. Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true)!.Cast<DictionaryEntry>()
            .Select(e => e.Value).OfType<string>().Where(v => v.Contains("&&", StringComparison.Ordinal)).Select(Shown)];

    /// <summary>
    /// The texts of the labels, buttons, check boxes, radio buttons and tab headers of <paramref name="window"/> that show
    /// an ampersand: text blocks drawn inside them, and their own string content (also of a page that is not shown now).
    /// </summary>
    private static List<string> LiteralAmpersands(Window window)
    {
        var found = new List<string>();
        var controls = window.GetVisualDescendants().Concat(((ILogical)window).GetLogicalDescendants().OfType<Visual>()).OfType<Control>().Distinct();
        foreach (var control in controls.Where(c => c is Label or Button or SplitButton or TabItem))
        {
            var texts = control.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text)
                .Append((control as ContentControl)?.Content as string)
                .Append((control as HeaderedContentControl)?.Header as string);
            foreach (var text in texts.OfType<string>().Where(t => t.Contains('&', StringComparison.Ordinal)))
            {
                // Access text: an underscore marks the access key ("__" is a literal one); an ampersand is always drawn.
                var shown = text.Replace("__", "\0", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).Replace('\0', '_');
                if (!DoubledInResources.Contains(shown))
                    found.Add($"{AutomationIds.Get(control) ?? control.GetType().Name}: '{text}'");
            }
        }

        return [.. found.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// I-286, UI-DLG-004: no label or button of a dialog draws the mnemonic marker of its resource text (<c>&amp;New</c>,
    /// <c>Move &amp;Up</c>) — the letter after it is the access key. An ampersand is shown only where the resource doubles it.
    /// </summary>
    [AvaloniaTheory(Timeout = 30000)]
    [MemberData(nameof(DialogNames))]
    [Trait("Req", "UI-DLG-004")]
    public async Task DialogLabels_ShowNoLiteralAmpersand(string dialog)
    {
        var (window, scene, open) = Dialogs[dialog];
        var (h, form) = await StartAsync(scene);
        await using var harness = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            var running = open(h, form);
            var dlg = await h.WaitForDialogAsync(dialog);
            Dispatcher.UIThread.RunJobs();
            Assert.IsType(window, dlg.Window);

            // The check sees the controls it is about: this dialog has a label or a button.
            Assert.Contains(dlg.Window.GetVisualDescendants(), v => v is Label or Button or SplitButton);

            var literal = LiteralAmpersands(dlg.Window);
            Assert.True(literal.Count == 0, $"{dialog}: the mnemonic marker is drawn: {string.Join("; ", literal)}");

            await dlg.Window.Model.PressCancelAsync();
            await h.WaitForAsync(() => !dlg.Window.IsVisible, $"dialog {dialog} closed");
            await running;
            Dispatcher.UIThread.RunJobs();
        });
    }

    /// <summary>The theory above has a case for every dialog window class of the IDE, so a new dialog cannot be left out.</summary>
    [Fact]
    [Trait("Req", "UI-DLG-004")]
    public void DialogLabels_TheoryCoversEveryDialogWindow()
    {
        var all = typeof(DialogWindow).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(DialogWindow))).Select(t => t.Name).Order(StringComparer.Ordinal);
        Assert.Equal(all, Dialogs.Values.Select(d => d.Window.Name).Order(StringComparer.Ordinal));
    }
}
