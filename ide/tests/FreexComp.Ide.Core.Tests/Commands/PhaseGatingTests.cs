using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Popups;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Commands;

/// <summary>Commands and menus follow the composition's implemented phase, not the product constant (I-213).</summary>
[Trait("Phase", "5")]
public sealed class PhaseGatingTests
{
    private static async Task<(IdeServices Services, TextDocumentViewModel Document)> OpenUnitAsync(int phase)
    {
        var fs = new InMemoryFileSystem();
        foreach (var (path, text) in (ReadOnlySpan<(string, string)>)[(DesignerFixture.UnitPath, DesignerFixture.Unit), (DesignerFixture.FormPath, DesignerFixture.Form)])
        {
            fs.CreateDirectory(Path.GetDirectoryName(path)!);
            fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        }

        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.ImplementedPhase = phase;
        });
        ((Core.Documents.DocumentService)services.Documents).DesignCatalog = CodeSyncTests.Catalog;
        var document = Assert.IsAssignableFrom<TextDocumentViewModel>((await services.Documents.OpenAsync(DesignerFixture.UnitPath)).Value);
        Assert.Same(document, services.Documents.ActiveDocument);
        return (services, document);
    }

    /// <summary>
    /// I-218: below P5 no form document is created even with a design catalogue — the unit opens as a plain text
    /// document and its <c>.form</c> opens as text (UI-TW-108 "before P5: opens the .form as text"); from P5 both open
    /// the form document.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-TW-108")]
    public Task NoFormDocumentBelowPhase5() => TestUiThread.RunAsync(NoFormDocumentBelowPhase5OnUiThread);

    private static async Task NoFormDocumentBelowPhase5OnUiThread()
    {
        var (services, unit) = await OpenUnitAsync(phase: 4);
        Assert.IsNotType<FormDocumentViewModel>(unit);
        var formText = Assert.IsAssignableFrom<TextDocumentViewModel>((await services.Documents.OpenAsync(DesignerFixture.FormPath)).Value);
        Assert.IsNotType<FormDocumentViewModel>(formText);
        Assert.Equal(DesignerFixture.FormPath, formText.FilePath);
        Assert.Equal(2, services.Documents.Documents.Count);
        Assert.DoesNotContain(services.Documents.Documents, d => d is FormDocumentViewModel);

        (services, unit) = await OpenUnitAsync(phase: 5);
        var form = Assert.IsType<FormDocumentViewModel>(unit);
        Assert.Same(form, (await services.Documents.OpenAsync(DesignerFixture.FormPath)).Value);
    }

    /// <summary>
    /// <c>view.toggleFormCode</c> / <c>view.formAsText</c> (P5) have their handlers only from P5 (before: the later-phase
    /// stub, never executable); the editor context menu omits later-phase items by the composition's phase (UI-MENU-042).
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-042")]
    public Task FormViewCommandsFollowPhase() => TestUiThread.RunAsync(FormViewCommandsFollowPhaseOnUiThread);

    private static async Task FormViewCommandsFollowPhaseOnUiThread()
    {
        var (services, document) = await OpenUnitAsync(phase: 4); // I-218: a plain text document below P5
        Assert.False(services.Commands.CanExecute("view.toggleFormCode"));
        Assert.False(services.Commands.CanExecute("view.formAsText"));
        await services.Commands.ExecuteAsync("view.toggleFormCode");
        Assert.Same(document, services.Documents.ActiveDocument);
        Assert.DoesNotContain(document.TextAreaMenu!(), i => i.CommandId == "view.toggleFormCode");

        (services, document) = await OpenUnitAsync(phase: 5);
        var form = Assert.IsType<FormDocumentViewModel>(document);
        Assert.True(services.Commands.CanExecute("view.toggleFormCode"));
        Assert.True(services.Commands.CanExecute("view.formAsText"));
        Assert.Contains(form.TextAreaMenu!(), i => i.CommandId == "view.toggleFormCode");
        await services.Commands.ExecuteAsync("view.formAsText");
        Assert.Equal(DocumentView.Text, form.ActiveView);
    }

    // ---- Phase 8 delivery gate (phase8-delivery.md, I-230) ----

    private const string GateDir = @"C:\p\Gate";
    private const string GateProject = GateDir + @"\Gate.pproj";

    private static readonly FocusState ProjectManagerFocus = new(CommandScope.ProjectManager, ProjectManagerViewModel.ToolName, null, false);
    private static readonly FocusState EditorFocus = new(CommandScope.Editor, null, null, false);

    /// <summary>A composition (the product's phase unless lowered) whose file system holds the project <c>Gate</c> (not opened).</summary>
    private static IdeServices BuildWithProject(int? phase = null)
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(GateDir);
        fs.WriteAllBytes(GateProject, Encoding.UTF8.GetBytes("""
            { "formatVersion": 1, "name": "Gate", "mainSource": "Gate.pas", "activeConfiguration": "Debug",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } } }
            """));
        fs.WriteAllBytes(GateDir + @"\Gate.pas", Encoding.UTF8.GetBytes("program Gate;\nbegin\nend.\n"));
        return TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.ImplementedPhase = phase;
        });
    }

    /// <summary>A command service of its own: the catalog, the given handlers, then the later-phase stubs of <paramref name="phase"/>.</summary>
    private static CommandService StandaloneCommands(IdeServices services, int phase, params (string Id, ICommandHandler Handler)[] handlers)
    {
        var commands = new CommandService(services.Keymap, new ImmediateDispatcher(), new ManualClock(), new RecordingLog(), new FakeDialogService(),
            new RecordingStatusService(), new FocusTracker());
        foreach (var (id, handler) in handlers)
        {
            commands.RegisterHandler(id, handler);
        }

        commands.RegisterLaterPhaseStubs(phase);
        return commands;
    }

    private sealed class RecordingHandler : ICommandHandler
    {
        public int Executions { get; private set; }

        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument)
        {
            Executions++;
            return Task.CompletedTask;
        }
    }

    private static bool PaletteLists(ICommandService commands, ILayoutService layout, FocusState origin, int phase, string id)
    {
        var palette = new CommandPaletteViewModel(commands, layout, origin, phase) { Input = commands.Get(id).Title };
        return palette.Items.Any(i => i.Id == id);
    }

    private static OptionsViewModel Options(IdeServices services, int phase) =>
        new(services.Settings, services.Keymap, services.Layout, services.Dialogs, services.FilePrompts, Core.Platform.ThemeKind.Light, null, phase);

    /// <summary>Null when <paramref name="id"/> names an existing phase-8 item in the form of IDE-P8-002; else what is wrong.</summary>
    private static string? Problem(string id, IdeServices services, OptionsViewModel options)
    {
        static string? Phase8(int? phase) => phase switch { null => "unknown", 8 => null, _ => "phase " + phase };
        if (id.StartsWith("options:", StringComparison.Ordinal))
        {
            var rest = id["options:".Length..];
            if (options.Pages.FirstOrDefault(p => p.Id == rest) is { } page)
                return Phase8(page.Phase);
            return Phase8(options.Pages.SelectMany(p => p.Fields.Where(f => p.Id + "." + f.Id == rest)).Select(f => (int?)f.Phase).FirstOrDefault());
        }

        if (id.StartsWith("toolwindow:", StringComparison.Ordinal))
            return Phase8(services.ToolWindows.Registered.Where(d => "toolwindow:" + d.Name == id).Select(d => (int?)d.Phase).FirstOrDefault());
        if (id.StartsWith("toolbar:", StringComparison.Ordinal))
            return Phase8(ToolBarDefinitions.All.Where(d => "toolbar:" + d.Name == id).Select(d => (int?)d.Phase).FirstOrDefault());
        if (id.StartsWith("setting:", StringComparison.Ordinal)) // the settings catalog carries no phase: existence only
            return SettingsCatalog.All.Any(k => "setting:" + k.Name == id) ? null : "unknown";
        return Phase8(CommandCatalog.All.Where(c => c.Id == id).Select(c => (int?)c.Phase).FirstOrDefault());
    }

    /// <summary>
    /// IDE-P8-001, 002, 005: the delivered list is in ordinal order without duplicates, compared ordinally, and every id
    /// names an existing phase-8 command, Options page or field, tool window, toolbar or setting.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-P8-001")]
    [Trait("Req", "IDE-P8-002")]
    [Trait("Req", "IDE-P8-005")]
    [Trait("Req", "IDE-P8-006")]
    public Task DeliveredIdsExistAndArePhase8() => TestUiThread.RunAsync(DeliveredIdsExistAndArePhase8OnUiThread);

    private static Task DeliveredIdsExistAndArePhase8OnUiThread()
    {
        var ids = Phase8Features.DeliveredIds;
        Assert.Equal(ids.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal), ids);
        Assert.Equal(ids.Count, Phase8Features.Delivered.Count);
        Assert.All(ids, id => Assert.Contains(id, Phase8Features.Delivered));

        var services = BuildWithProject();
        var options = Options(services, services.ImplementedPhase);
        Assert.All(ids, id => Assert.Null(Problem(id, services, options)));

        // The check itself: wrong ids are found, right ones pass (the set starts empty, IDE-P8-006).
        Assert.Null(Problem("project.setActive", services, options));
        Assert.Null(Problem("options:Environment.General.Language", services, options));
        Assert.Null(Problem("setting:environment.language", services, options));
        Assert.Equal("unknown", Problem("project.setActiv", services, options));
        Assert.Equal("phase 1", Problem("file.save", services, options));
        Assert.Equal("phase 1", Problem("options:Environment.General", services, options));
        Assert.Equal("phase 1", Problem("options:Environment.General.Theme", services, options));
        Assert.Equal("unknown", Problem("options:Environment.General.Nope", services, options));
        Assert.Equal("phase 1", Problem("toolwindow:ProjectManager", services, options));
        Assert.Equal("unknown", Problem("toolwindow:Nope", services, options));
        Assert.Equal("phase 1", Problem("toolbar:Standard", services, options));
        Assert.Equal("unknown", Problem("setting:no.such", services, options));

        // The set compares ordinally (IDE-P8-001); the test seam ends with its scope.
        using (Phase8Features.WithDelivered("project.setActive"))
        {
            Assert.Contains("project.setActive", Phase8Features.Delivered);
            Assert.DoesNotContain("project.setactive", Phase8Features.Delivered);
        }

        Assert.Equal(ids.Count, Phase8Features.Delivered.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// IDE-P8-003: a delivered phase-8 command with its handler is listed by the command palette, is not a "later version"
    /// row of Key Mappings, appears in the Project Manager and editor context menus that list it, and runs its handler.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-P8-003")]
    public Task DeliveredCommandIsListedAndRoutedEverywhere() => TestUiThread.RunAsync(DeliveredCommandIsListedAndRoutedEverywhereOnUiThread);

    private static async Task DeliveredCommandIsListedAndRoutedEverywhereOnUiThread()
    {
        const int Phase = ServiceGraph.ImplementedPhase;
        var services = BuildWithProject(); // its own commands are not used: project.newFolder has no product handler yet
        using var delivered = Phase8Features.WithDelivered("project.newFolder", "refactor.rename");
        var folder = new RecordingHandler();
        var rename = new RecordingHandler();
        var commands = StandaloneCommands(services, Phase, ("project.newFolder", folder), ("refactor.rename", rename));

        // Handlers, not stubs.
        Assert.True(commands.CanExecute("project.newFolder"));
        Assert.False(commands.CanExecute("project.setActive")); // phase 8, not delivered: the stub

        // Command palette (origin: the Project Manager / the editor).
        Assert.True(PaletteLists(commands, services.Layout, ProjectManagerFocus, Phase, "project.newFolder"));
        Assert.True(PaletteLists(commands, services.Layout, EditorFocus, Phase, "refactor.rename"));
        Assert.False(PaletteLists(commands, services.Layout, ProjectManagerFocus, Phase, "project.setActive"));

        // Key Mappings.
        var keys = new KeyMappingsPageViewModel(services.Keymap, services.Dialogs, Phase);
        Assert.False(keys.Rows.Single(r => r.Command.Id == "project.newFolder").IsLaterPhase);
        Assert.False(keys.Rows.Single(r => r.Command.Id == "refactor.rename").IsLaterPhase);
        Assert.True(keys.Rows.Single(r => r.Command.Id == "project.setActive").IsLaterPhase);

        // Project Manager context menu of the project row; the item runs the handler.
        var projectManager = new ProjectManagerViewModel(services.Projects, services.Documents, services.FileSystem, services.Dialogs, commands,
            services.Log, Phase);
        Assert.True(await services.Projects.OpenAsync(GateProject));
        projectManager.Select(projectManager.Roots[0]);
        var item = Assert.Single(projectManager.ContextMenu(), i => i.CommandId == "project.newFolder");
        Assert.True(item.Command!.CanExecute(null));
        item.Command.Execute(null);
        Assert.Equal(1, folder.Executions);

        // Editor context menu.
        var editorItem = Assert.Single(EditorContextMenu.Build(commands, Phase, hasForm: false), i => i.CommandId == "refactor.rename");
        editorItem.Command!.Execute(null);
        Assert.Equal(1, rename.Executions);

        // The composition: a delivered Editor command without a service handler gets the focused-target route like the
        // implemented ones (EditorCommandRoute), so the start-up check passes and the document's own menu lists it.
        using var routed = Phase8Features.WithDelivered("refactor.rename");
        var (_, document) = await OpenUnitAsync(Phase);
        Assert.Contains(document.TextAreaMenu!(), i => i.CommandId == "refactor.rename");
        Assert.DoesNotContain(document.TextAreaMenu!(), i => i.CommandId == "search.findReferences"); // phase 8, not delivered
    }

    /// <summary>
    /// IDE-P8-004 (second sentence): a phase-8 command outside the set is as before — stub handler, absent from the
    /// palette and the context menus, a "later version" row of Key Mappings.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-P8-004")]
    public Task UndeliveredPhase8CommandStaysStub() => TestUiThread.RunAsync(UndeliveredPhase8CommandStaysStubOnUiThread);

    private static async Task UndeliveredPhase8CommandStaysStubOnUiThread()
    {
        Assert.DoesNotContain("project.setActive", Phase8Features.Delivered);
        var services = BuildWithProject();
        var commands = services.Commands;
        var phase = services.ImplementedPhase;
        Assert.True(await services.Projects.OpenAsync(GateProject));
        services.FocusTracker.Update(ProjectManagerFocus);
        var projectManager = services.ProjectManager;
        projectManager.Select(projectManager.Roots[0]);

        Assert.True(commands.IsHandled("project.setActive"));
        Assert.False(commands.CanExecute("project.setActive"));
        await commands.ExecuteAsync("project.setActive"); // a no-op

        Assert.True(PaletteLists(commands, services.Layout, ProjectManagerFocus, phase, "project.addNewUnit")); // the control
        Assert.False(PaletteLists(commands, services.Layout, ProjectManagerFocus, phase, "project.setActive"));

        var menu = projectManager.ContextMenu();
        Assert.Contains(menu, i => i.CommandId == "project.addNewUnit");
        Assert.DoesNotContain(menu, i => i.CommandId == "project.setActive");
        Assert.All(menu.Where(i => i.Command is not null),
            i => Assert.True(Phase8Features.IsAvailable(i.CommandId!, i.Command!.Definition.Phase, phase), i.CommandId));
        Assert.All(EditorContextMenu.Build(commands, phase, hasForm: true).Where(i => i.Command is not null),
            i => Assert.True(Phase8Features.IsAvailable(i.CommandId!, i.Command!.Definition.Phase, phase), i.CommandId));

        var keys = new KeyMappingsPageViewModel(services.Keymap, services.Dialogs, phase);
        Assert.True(keys.Rows.Single(r => r.Command.Id == "project.setActive").IsLaterPhase);
    }

    /// <summary>
    /// IDE-P8-004 (first sentence): a delivered command needs a real handler before the stubs are registered — without
    /// one, or on the stub handler, the start-up check fails.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-P8-004")]
    public Task DeliveredCommandWithoutHandlerFailsVerification() => TestUiThread.RunAsync(DeliveredCommandWithoutHandlerFailsVerificationOnUiThread);

    private static Task DeliveredCommandWithoutHandlerFailsVerificationOnUiThread()
    {
        const int Phase = ServiceGraph.ImplementedPhase;
        var services = BuildWithProject();
        // Every command the product treats as implemented: the earlier phases and the delivered phase-8 ones (F09-054 on).
        (string, ICommandHandler)[] implemented = [.. CommandCatalog.All.Where(c => Phase8Features.IsAvailable(c.Id, c.Phase, Phase))
            .Select(c => (c.Id, (ICommandHandler)new RecordingHandler()))];

        // Not delivered: the stub satisfies the check.
        var stubbed = StandaloneCommands(services, Phase, implemented);
        stubbed.VerifyAllHandled();

        using (Phase8Features.WithDelivered("project.setActive"))
        {
            // Delivered after the stubs were registered: the command sits on the stub handler.
            var onStub = Assert.Throws<InvalidOperationException>(stubbed.VerifyAllHandled);
            Assert.Contains("project.setActive", onStub.Message, StringComparison.Ordinal);

            // Delivered without a handler: no stub is given, the check names the command.
            var missing = StandaloneCommands(services, Phase, implemented);
            Assert.False(missing.IsHandled("project.setActive"));
            Assert.True(missing.IsHandled("project.newFolder")); // phase 8, not delivered: the stub
            var noHandler = Assert.Throws<InvalidOperationException>(missing.VerifyAllHandled);
            Assert.Contains("project.setActive", noHandler.Message, StringComparison.Ordinal);

            // With its handler the check passes; the composition refuses to start without it.
            StandaloneCommands(services, Phase, [.. implemented, ("project.setActive", new RecordingHandler())]).VerifyAllHandled();
            Assert.Throws<InvalidOperationException>(() => BuildWithProject());
        }

        return Task.CompletedTask;
    }

    /// <summary>IDE-P8-003: the gate follows the composition's phase — below phase 7 a delivered id is unavailable.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-P8-003")]
    public Task LoweredPhaseHidesDeliveredItems() => TestUiThread.RunAsync(LoweredPhaseHidesDeliveredItemsOnUiThread);

    private static async Task LoweredPhaseHidesDeliveredItemsOnUiThread()
    {
        Assert.False(Phase8Features.IsAvailable("refactor.rename", 8, 7)); // not delivered
        Assert.True(Phase8Features.IsAvailable("refactor.rename", 8, 8)); // the phase itself
        Assert.True(Phase8Features.IsAvailable("file.save", 1, 1));
        Assert.False(Phase8Features.IsAvailable("view.toggleFormCode", 5, 4));

        using var delivered = Phase8Features.WithDelivered("options:Environment.General.Language", "refactor.rename", "toolbar:Nine", "toolwindow:Todo");
        Assert.True(Phase8Features.IsAvailable("refactor.rename", 8, 7));
        Assert.True(Phase8Features.IsAvailable("toolwindow:Todo", 8, 7));
        Assert.False(Phase8Features.IsAvailable("refactor.rename", 8, 6));
        Assert.False(Phase8Features.IsAvailable("refactor.rename", 8, 5));
        Assert.False(Phase8Features.IsAvailable("toolbar:Nine", 9, 7)); // only phase 8 is delivered card by card
        Assert.False(Phase8Features.IsAvailable("project.setActive", 8, 7));

        // A composition pinned to phase 5: the delivered command is a stub again (and the start-up check accepts that),
        // hidden from the palette and the editor menu, a "later version" row; the delivered Options field stays disabled.
        var (services, document) = await OpenUnitAsync(phase: 5);
        Assert.False(services.Commands.CanExecute("refactor.rename"));
        Assert.DoesNotContain(document.TextAreaMenu!(), i => i.CommandId == "refactor.rename");
        Assert.True(PaletteLists(services.Commands, services.Layout, EditorFocus, 5, "edit.selectAll")); // the control
        Assert.False(PaletteLists(services.Commands, services.Layout, EditorFocus, 5, "refactor.rename"));
        Assert.True(new KeyMappingsPageViewModel(services.Keymap, services.Dialogs, 5).Rows.Single(r => r.Command.Id == "refactor.rename").IsLaterPhase);
        Assert.False(Options(services, 5).Field("Language").IsEnabled);

        // The same ids at the product's phase.
        (services, document) = await OpenUnitAsync(ServiceGraph.ImplementedPhase);
        Assert.Contains(document.TextAreaMenu!(), i => i.CommandId == "refactor.rename");
        Assert.True(PaletteLists(services.Commands, services.Layout, EditorFocus, ServiceGraph.ImplementedPhase, "refactor.rename"));
        Assert.True(Options(services, ServiceGraph.ImplementedPhase).Field("Language").IsEnabled);
    }
}
