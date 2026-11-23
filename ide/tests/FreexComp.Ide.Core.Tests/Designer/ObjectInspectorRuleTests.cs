using FreexComp.Designer.Inspector;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using static FreexComp.Ide.Core.Tests.Designer.DesignerFixture;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// Object Inspector rules that had no test of their own (object-inspector.md §1, §2, §4, §8, §10; ui/designer.md §8;
/// D-087): instance selector, arrangement toggle, filter timing, read-only rows, the Events page of preserved components
/// and read-only forms, and the description pane.
/// </summary>
[Trait("Phase", "5")]
public sealed class ObjectInspectorRuleTests
{
    private readonly DesignerFixture _f = new();

    private async Task<(FormDocumentViewModel Document, FormDesignerViewModel Designer, ObjectInspectorViewModel Inspector)> OpenAsync(
        string? unit = null, string? form = null)
    {
        var document = await _f.OpenAsync(unit, form);
        var inspector = new ObjectInspectorViewModel(_f.Services.Status, _f.Services.Settings);
        inspector.Bind(document.Designer);
        return (document, document.Designer, inspector);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-002")]
    [Trait("Req", "UI-DSG-101")]
    public Task InstanceSelectorListsFormFirstThenByName() => TestUiThread.RunAsync(InstanceSelectorListsFormFirstThenByNameOnUiThread);

    private async Task InstanceSelectorListsFormFirstThenByNameOnUiThread()
    {
        var (_, designer, inspector) = await OpenAsync();

        // The form first, then the others by name (ignoring case); item text "Name: TClassName".
        Assert.Equal(["MainForm: TMainForm", "GreetButton: TButton", "InfoLabel: TLabel", "NameEdit: TEdit", "OkButton: TButton", "Panel1: TPanel"],
            inspector.ComponentItems);
        Assert.Equal(0, inspector.SelectedComponentIndex);
        Assert.Null(inspector.MultiSelectionText);

        // Choosing an entry sets the designer selection.
        inspector.SelectComponent(3);
        Assert.Equal(["NameEdit"], designer.Surface!.Selection.Items.Select(o => o.Name));
        Assert.Equal(3, inspector.SelectedComponentIndex);

        // Several selected: no entry, the closed combo shows "{n} components selected".
        Select(designer, "NameEdit", "OkButton", "Panel1");
        Assert.Equal(-1, inspector.SelectedComponentIndex);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Designer_SelectionMany, 3), inspector.MultiSelectionText);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-095")]
    public Task GeometryRowsFollowADragAtMostEvery50Ms() => TestUiThread.RunAsync(GeometryRowsFollowADragAtMostEvery50MsOnUiThread);

    private async Task GeometryRowsFollowADragAtMostEvery50MsOnUiThread()
    {
        var document = await _f.OpenAsync();
        var designer = document.Designer;
        var inspector = (ObjectInspectorViewModel)_f.Services.ToolWindows.Get(ObjectInspectorViewModel.ToolName)!;
        var clock = (ManualClock)_f.Services.Clock;
        _f.Services.Settings.Set(SettingsCatalog.DesignerSnapToGrid, false);
        _f.Services.Settings.Set(SettingsCatalog.DesignerUseGuidelines, false);
        Select(designer, "OkButton");
        var surface = designer.Surface!;
        string Value(string path) => inspector.Rows.Single(r => r.Kind == PropertyRowKind.Property && r.Path == path).Display;
        var changes = 0;
        inspector.PropertyChanged += (_, e) => changes += e.PropertyName == nameof(ObjectInspectorViewModel.Rows) ? 1 : 0;
        Assert.Equal(("232", "48"), (Value("Left"), Value("Top")));

        // A drag of OkButton: the rows show the preview position, refreshed once per 50 ms however many moves arrive.
        var b = surface.Geometry.GetBounds(Object(designer, "OkButton"));
        var (x, y) = (b.Left + 10, b.Top + 10);
        void Pointer(PointerKind kind, int dx, int dy) =>
            surface.OnPointer(new PointerInput(kind, x + dx, y + dy, PointerButton.Left, InputModifiers.None));
        Pointer(PointerKind.Down, 0, 0);
        Pointer(PointerKind.Move, 10, 5);
        Pointer(PointerKind.Move, 20, 7);
        Assert.Equal(InteractionState.Moving, surface.State);
        changes = 0;
        Pointer(PointerKind.Move, 21, 9);
        Assert.Equal(0, changes);
        clock.Advance(TimeSpan.FromMilliseconds(49));
        Assert.Equal(0, changes);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, changes);
        Assert.Equal(("253", "57"), (Value("Left"), Value("Top")));
        Assert.Equal("232", Stored(designer, "OkButton", "Left")); // the model is not changed by the preview
        Pointer(PointerKind.Move, 30, 9);
        Pointer(PointerKind.Move, 31, 10);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(2, changes);
        Assert.Equal(("263", "58"), (Value("Left"), Value("Top")));

        // The drop commits: the rows show the stored values; no preview refresh is left pending.
        Pointer(PointerKind.Up, 31, 10);
        Assert.Equal("263", Stored(designer, "OkButton", "Left"));
        Assert.Equal(("263", "58"), (Value("Left"), Value("Top")));
        clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(("263", "58"), (Value("Left"), Value("Top")));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-012")]
    public Task ToggleCategorizedPersistsTheSetting() => TestUiThread.RunAsync(ToggleCategorizedPersistsTheSettingOnUiThread);

    private async Task ToggleCategorizedPersistsTheSettingOnUiThread()
    {
        var (_, designer, inspector) = await OpenAsync();
        Select(designer, "GreetButton");
        Assert.False(inspector.IsArrangedByName);
        Assert.Contains(inspector.Rows, r => r.Kind == PropertyRowKind.Category);

        inspector.ToggleArrangement();
        Assert.True(inspector.IsArrangedByName);
        Assert.Equal("name", _f.Services.Settings.Get(SettingsCatalog.ObjectInspectorArrangement));
        Assert.DoesNotContain(inspector.Rows, r => r.Kind == PropertyRowKind.Category);

        // A second inspector on the same settings starts in the persisted arrangement.
        var other = new ObjectInspectorViewModel(_f.Services.Status, _f.Services.Settings);
        Assert.True(other.IsArrangedByName);

        inspector.ToggleArrangement();
        Assert.Equal("category", _f.Services.Settings.Get(SettingsCatalog.ObjectInspectorArrangement));
        Assert.Contains(inspector.Rows, r => r.Kind == PropertyRowKind.Category);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-021")]
    public Task FilterAppliesAfter150MsKeptOnSelectionClearedOnDocumentChange() =>
        TestUiThread.RunAsync(FilterAppliesAfter150MsKeptOnSelectionClearedOnDocumentChangeOnUiThread);

    private async Task FilterAppliesAfter150MsKeptOnSelectionClearedOnDocumentChangeOnUiThread()
    {
        var document = await _f.OpenAsync();
        var designer = document.Designer;
        var inspector = (ObjectInspectorViewModel)_f.Services.ToolWindows.Get(ObjectInspectorViewModel.ToolName)!;
        var clock = (ManualClock)_f.Services.Clock;
        Assert.Same(designer, BoundDesigner(inspector));
        Select(designer, "GreetButton");
        int Properties() => inspector.Rows.Count(r => r.Kind == PropertyRowKind.Property);
        var all = Properties();

        // Not before 150 ms without typing; each keystroke restarts the delay.
        inspector.FilterText = "Capt";
        clock.Advance(TimeSpan.FromMilliseconds(149));
        Assert.Equal(all, Properties());
        inspector.FilterText = "Capti";
        clock.Advance(TimeSpan.FromMilliseconds(149));
        Assert.Equal(all, Properties());
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["Caption"], inspector.Rows.Where(r => r.Kind == PropertyRowKind.Property).Select(r => r.Path));

        // Kept when the selection changes.
        Select(designer, "OkButton");
        Assert.Equal("Capti", inspector.FilterText);
        Assert.Equal(["Caption"], inspector.Rows.Where(r => r.Kind == PropertyRowKind.Property).Select(r => r.Path));

        // Cleared when the active document changes.
        _f.File(Dir + @"\Second.pas", Unit.Replace("MainForm", "SecondForm", StringComparison.Ordinal));
        _f.File(Dir + @"\Second.form", Form.Replace("MainForm", "SecondForm", StringComparison.Ordinal));
        var second = Assert.IsType<FormDocumentViewModel>((await _f.Services.Documents.OpenAsync(Dir + @"\Second.pas")).Value);
        Assert.True(second.ShowView(DocumentView.Design));
        Assert.Same(second.Designer, BoundDesigner(inspector));
        Assert.Equal("", inspector.FilterText);
        Select(second.Designer, "GreetButton");
        Assert.True(Properties() > 1);
    }

    private static FormDesignerViewModel? BoundDesigner(ObjectInspectorViewModel inspector) =>
        (FormDesignerViewModel?)typeof(ObjectInspectorViewModel)
            .GetField("_designer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(inspector);

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-036")]
    public Task ReadOnlyRowsCanBeCurrentAndCopiedButNotEdited() => TestUiThread.RunAsync(ReadOnlyRowsCanBeCurrentAndCopiedButNotEditedOnUiThread);

    private async Task ReadOnlyRowsCanBeCurrentAndCopiedButNotEditedOnUiThread()
    {
        var (_, designer, inspector) = await OpenAsync();
        var clipboard = new FakeClipboard();
        inspector.AttachCommands(_f.Services.Commands, clipboard);
        var target = (ICommandTarget)inspector;

        // A readOnly metadata property of the form.
        Select(designer, "MainForm");
        inspector.CurrentPath = "PixelsPerInch";
        Assert.Equal("PixelsPerInch", inspector.CurrentPath);
        Assert.True(inspector.Rows.Single(r => r.Path == "PixelsPerInch").IsReadOnly);
        Assert.False(inspector.BeginEdit());
        Assert.False(inspector.IsEditing);
        Assert.True(target.CanExecute("edit.copy", null));
        Assert.True(await target.ExecuteAsync("edit.copy", null));
        Assert.Equal("96", clipboard.Text);

        // The whole form read-only: rows stay current and copyable, nothing enters edit mode.
        designer.Surface!.IsReadOnly = true;
        Select(designer, "GreetButton");
        inspector.CurrentPath = "Caption";
        Assert.Equal("Caption", inspector.CurrentPath);
        Assert.False(inspector.BeginEdit());
        Assert.True(await target.ExecuteAsync("edit.copy", null));
        Assert.Equal("&Greet", clipboard.Text);
        Assert.False(target.CanExecute("edit.paste", null));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-042")]
    public Task CommittedExpressionShowsTheEvaluatedNumber() => TestUiThread.RunAsync(CommittedExpressionShowsTheEvaluatedNumberOnUiThread);

    private async Task CommittedExpressionShowsTheEvaluatedNumberOnUiThread()
    {
        var (_, designer, inspector) = await OpenAsync();
        Select(designer, "GreetButton");
        inspector.CurrentPath = "Width";
        Assert.True(inspector.BeginEdit());
        inspector.EditText = "75 + 2 * 10";
        Assert.True(inspector.Commit());
        Assert.Equal("95", Stored(designer, "GreetButton", "Width"));
        Assert.Equal("95", inspector.Rows.Single(r => r.Path == "Width").Display);

        // Editing again starts from the number, not from the expression.
        Assert.True(inspector.BeginEdit());
        Assert.Equal("95", inspector.EditText);
        inspector.Cancel();
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-005")]
    [Trait("Req", "DSG-OI-087")]
    public Task EventsPageOfPreservedComponentAndReadOnlyForm() => TestUiThread.RunAsync(EventsPageOfPreservedComponentAndReadOnlyFormOnUiThread);

    private async Task EventsPageOfPreservedComponentAndReadOnlyFormOnUiThread()
    {
        var unit = Unit.Replace("    Panel1: TPanel;\r\n", "    Panel1: TPanel;\r\n    Fancy1: TFancyWidget;\r\n", StringComparison.Ordinal);
        var form = Form.Replace("end\n", "  object Fancy1: TFancyWidget\n    Left = 250\n    Top = 150\n    Width = 50\n    Height = 30\n  end\nend\n", StringComparison.Ordinal);
        var (_, designer, inspector) = await OpenAsync(unit, form);
        inspector.Page = InspectorPage.Events;

        // A preserved component has no events: the empty state Msg.Inspector.NoEvents.
        Select(designer, "Fancy1");
        Assert.True(Object(designer, "Fancy1").IsPreserved);
        Assert.True(inspector.HasNoEvents);
        Assert.Empty(inspector.EventRows);
        Assert.Equal(Strings.Msg_Inspector_NoEvents, inspector.EmptyStateText);

        // Read-only form: event rows are read-only (no edit, no new handler), but an assigned event still navigates.
        designer.Surface!.IsReadOnly = true;
        Select(designer, "GreetButton");
        Assert.True(inspector.IsEventsReadOnly);
        Assert.False(inspector.CommitEvent("OnClick", "OtherClick"));
        Assert.Equal("GreetButtonClick", Stored(designer, "GreetButton", "OnClick"));
        inspector.ActivateEvent("OnKeyPress"); // empty: nothing is created
        Assert.Null(Stored(designer, "GreetButton", "OnKeyPress"));
        Assert.Equal(DocumentView.Design, designer.ActiveView);
        inspector.ActivateEvent("OnClick");
        Assert.Equal(DocumentView.Code, designer.ActiveView);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-100")]
    [Trait("Req", "UI-DSG-110")]
    public Task DescriptionPaneShowsNameAndTypeOrSignature() => TestUiThread.RunAsync(DescriptionPaneShowsNameAndTypeOrSignatureOnUiThread);

    private async Task DescriptionPaneShowsNameAndTypeOrSignatureOnUiThread()
    {
        var (_, designer, inspector) = await OpenAsync();
        Select(designer, "GreetButton");

        // Without a metadata description: "Name: TypeName".
        inspector.CurrentPath = "Align";
        Assert.Equal("Align", inspector.DescriptionTitle);
        Assert.Equal("Align: TAlign", inspector.DescriptionText);
        inspector.CurrentPath = "Caption";
        Assert.Equal("Caption: string", inspector.DescriptionText, ignoreCase: true);

        // Events: the signature text from eventSignatures.
        inspector.Page = InspectorPage.Events;
        inspector.CurrentEvent = "OnClick";
        Assert.Equal("OnClick", inspector.DescriptionTitle);
        Assert.Equal("procedure(Sender: TObject)", inspector.DescriptionText);

        // objectInspector.showDescription hides the pane (persisted).
        Assert.True(inspector.ShowDescription);
        inspector.ToggleDescription();
        Assert.False(inspector.ShowDescription);
        Assert.False(_f.Services.Settings.Get(SettingsCatalog.ObjectInspectorShowDescription));
        inspector.ToggleDescription();
    }
}
