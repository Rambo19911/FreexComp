using System.Globalization;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using static FreexComp.Ide.Core.Tests.Designer.DesignerFixture;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The Object Inspector's Events page (object-inspector.md §8; D-076).</summary>
[Trait("Phase", "5")]
public sealed class EventsPageTests
{
    private readonly DesignerFixture _f = new();

    private async Task<(FormDocumentViewModel Document, FormDesignerViewModel Designer, ObjectInspectorViewModel Inspector)> OpenAsync(
        string? unit = null, string? form = null)
    {
        var document = await _f.OpenAsync(unit, form);
        var inspector = new ObjectInspectorViewModel(_f.Services.Status) { Page = InspectorPage.Events };
        inspector.Bind(document.Designer);
        return (document, document.Designer, inspector);
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static string Value(ObjectInspectorViewModel inspector, string eventName) => inspector.EventRows.Single(r => r.Name == eventName).Value;

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-082")]
    [Trait("Req", "DSG-OI-083")]
    public async Task DefaultHandlerName()
    {
        // GreetButtonKeyPress exists with the TNotifyEvent signature: the TKeyPressEvent handler gets the suffix 2.
        var unit = DesignerFixture.Unit
            .Replace("    procedure SharedClick(Sender: TObject);", "    procedure SharedClick(Sender: TObject);\r\n    procedure GreetButtonKeyPress(Sender: TObject);", StringComparison.Ordinal)
            .Replace("procedure TMainForm.SharedClick(Sender: TObject);", "procedure TMainForm.GreetButtonKeyPress(Sender: TObject);\r\nbegin\r\n\r\nend;\r\n\r\nprocedure TMainForm.SharedClick(Sender: TObject);", StringComparison.Ordinal);
        var (document, designer, inspector) = await OpenAsync(unit);

        Select(designer, "MainForm");
        Assert.Equal("FormCreate", inspector.DefaultHandlerName("OnCreate"));
        Select(designer, "OkButton");
        Assert.Equal("OkButtonClick", inspector.DefaultHandlerName("OnClick"));
        Select(designer, "GreetButton");
        Assert.Equal("GreetButtonKeyPress2", inspector.DefaultHandlerName("OnKeyPress"));
        Select(designer, "NameEdit");
        Assert.Equal("NameEditKeyPress", inspector.DefaultHandlerName("OnKeyPress")); // exists and matches: reused

        // Double-click on an empty row whose default name is a matching method: linked, no new stub, the Code view opens.
        var text = document.Buffer.Text;
        inspector.ActivateEvent("OnKeyPress");
        Assert.Equal("NameEditKeyPress", Stored(designer, "NameEdit", "OnKeyPress"));
        Assert.Equal(text, document.Buffer.Text);
        Assert.Equal(DocumentView.Code, designer.ActiveView);

        // On an empty row with a free name: the handler is created, assigned in one "Event" unit and shown.
        Assert.True(document.ShowView(DocumentView.Design));
        Select(designer, "OkButton");
        var count = designer.Undo.UndoCount;
        inspector.ActivateEvent("OnClick");
        Assert.Equal("OkButtonClick", Stored(designer, "OkButton", "OnClick"));
        Assert.Contains("    procedure OkButtonClick(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("procedure TMainForm.OkButtonClick(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(count + 1, designer.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Event", designer.Undo.UndoTitleKey);
        Assert.Equal(DocumentView.Code, designer.ActiveView);

        // On a row with a value: only navigation.
        Assert.True(document.ShowView(DocumentView.Design));
        Select(designer, "GreetButton");
        count = designer.Undo.UndoCount;
        inspector.ActivateEvent("OnClick");
        Assert.Equal(count, designer.Undo.UndoCount);
        Assert.Equal(DocumentView.Code, designer.ActiveView);
    }

    [Theory(Timeout = 30000)]
    [Trait("Req", "DSG-OI-084")]
    [Trait("Req", "DSG-OI-085")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public async Task CommitDecisionTable(int row)
    {
        // Row 9's other reference: OkButton.OnClick names GreetButtonClick too.
        var form = row == 9 ? DesignerFixture.Form.Replace("    Caption = 'OkButton'\n", "    Caption = 'OkButton'\n    OnClick = GreetButtonClick\n", StringComparison.Ordinal)
            : DesignerFixture.Form;
        var (document, designer, inspector) = await OpenAsync(form: form);
        var code = document.Buffer.Text;
        var count = designer.Undo.UndoCount;

        void Unchanged()
        {
            Assert.Equal(code, document.Buffer.Text);
            Assert.Equal(count, designer.Undo.UndoCount);
        }

        void OneUnit()
        {
            Assert.Equal(count + 1, designer.Undo.UndoCount);
            Assert.Equal("Undo_Designer_Event", designer.Undo.UndoTitleKey);
        }

        switch (row)
        {
            case 1: // T empty, V empty: nothing
                Select(designer, "OkButton");
                Assert.True(inspector.CommitEvent("OnClick", "  "));
                Unchanged();
                break;
            case 2: // T = V: nothing
                Select(designer, "GreetButton");
                Assert.True(inspector.CommitEvent("OnClick", "greetbuttonclick"));
                Unchanged();
                Assert.Equal("GreetButtonClick", Stored(designer, "GreetButton", "OnClick"));
                break;
            case 3: // T empty, V set: unassigned, the method stays and becomes a removal candidate
                Select(designer, "GreetButton");
                Assert.True(inspector.CommitEvent("OnClick", ""));
                Assert.Null(Stored(designer, "GreetButton", "OnClick"));
                Assert.Equal(code, document.Buffer.Text);
                Assert.Contains("GreetButtonClick", designer.CodeSync.HandlerCandidates);
                OneUnit();
                break;
            case 4: // not an identifier, or a reserved word
                Select(designer, "OkButton");
                Assert.False(inspector.CommitEvent("OnClick", "1Click"));
                Assert.Equal(Strings.Msg_Designer_HandlerNameInvalid, inspector.EditError);
                Assert.False(inspector.CommitEvent("OnClick", "end"));
                Assert.Equal(Strings.Msg_Designer_HandlerNameInvalid, inspector.EditError);
                Unchanged();
                break;
            case 5: // an existing compatible method: assigned without a code change; the old one becomes a candidate
                Select(designer, "OkButton");
                Assert.True(inspector.CommitEvent("OnClick", "sharedclick"));
                Assert.Equal("SharedClick", Stored(designer, "OkButton", "OnClick"));
                Select(designer, "GreetButton");
                Assert.True(inspector.CommitEvent("OnClick", "SharedClick"));
                Assert.Equal("SharedClick", Stored(designer, "GreetButton", "OnClick"));
                Assert.Equal(code, document.Buffer.Text);
                Assert.Contains("GreetButtonClick", designer.CodeSync.HandlerCandidates);
                Assert.Equal(count + 2, designer.Undo.UndoCount);
                break;
            case 6: // V empty, T a member that is not a compatible method
                Select(designer, "OkButton");
                Assert.False(inspector.CommitEvent("OnClick", "NameEditKeyPress"));
                Assert.Equal(Format(Strings.Msg_Designer_HandlerSignatureMismatch, "NameEditKeyPress", "OnClick", "procedure(Sender: TObject)"),
                    inspector.EditError);
                Assert.False(inspector.CommitEvent("OnClick", "FCount"));
                Assert.Equal(Format(Strings.Msg_Designer_NameConflictsWithMember, "TMainForm", "FCount"), inspector.EditError);
                Unchanged();
                break;
            case 7: // T new, V empty: created and assigned, the Code view is not opened
                Select(designer, "OkButton");
                Assert.True(inspector.CommitEvent("OnClick", "OkClicked"));
                Assert.Equal("OkClicked", Stored(designer, "OkButton", "OnClick"));
                Assert.Contains("    procedure OkClicked(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
                Assert.Contains("procedure TMainForm.OkClicked(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
                Assert.Equal(DocumentView.Design, designer.ActiveView);
                OneUnit();
                break;
            case 8: // T new, V set, referenced only here, method V exists: rename
                Select(designer, "GreetButton");
                Assert.True(inspector.CommitEvent("OnClick", "Greet2Click"));
                Assert.Equal("Greet2Click", Stored(designer, "GreetButton", "OnClick"));
                Assert.Contains("    procedure Greet2Click(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
                Assert.Contains("procedure TMainForm.Greet2Click(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
                Assert.DoesNotContain("GreetButtonClick", document.Buffer.Text, StringComparison.Ordinal);
                OneUnit();
                designer.Surface!.Execute(FreexComp.Designer.Surface.Commands.Undo);
                Assert.Equal(code, document.Buffer.Text);
                Assert.Equal("GreetButtonClick", Stored(designer, "GreetButton", "OnClick"));
                break;
            case 9: // T new, V referenced elsewhere: a new handler for this event only
                Select(designer, "GreetButton");
                Assert.True(inspector.CommitEvent("OnClick", "NewClick"));
                Assert.Equal("NewClick", Stored(designer, "GreetButton", "OnClick"));
                Assert.Equal("GreetButtonClick", Stored(designer, "OkButton", "OnClick"));
                Assert.Contains("procedure TMainForm.NewClick(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
                Assert.Contains("procedure TMainForm.GreetButtonClick(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
                OneUnit();
                break;
            case 10: // T exists with another signature, V set
                Select(designer, "GreetButton");
                Assert.False(inspector.CommitEvent("OnClick", "NameEditKeyPress"));
                Assert.Equal(Format(Strings.Msg_Designer_HandlerRenameConflict, "TMainForm", "NameEditKeyPress"), inspector.EditError);
                Unchanged();
                Assert.Equal("GreetButtonClick", Stored(designer, "GreetButton", "OnClick"));
                break;
        }
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-080")]
    [Trait("Req", "DSG-OI-081")]
    public async Task ListsCompatibleMethods()
    {
        var (_, designer, inspector) = await OpenAsync();
        Select(designer, "GreetButton");

        // DSG-OI-080: the flattened events by name, with their handler names.
        Assert.Equal(["OnClick", "OnKeyPress"], inspector.EventRows.Select(r => r.Name));
        Assert.Equal("GreetButtonClick", Value(inspector, "OnClick"));
        Assert.Equal("", Value(inspector, "OnKeyPress"));

        // DSG-OI-081: the methods of the form class whose signature matches, by name.
        Assert.Equal(["GreetButtonClick", "SharedClick"], inspector.EventMethods("OnClick"));
        Assert.Equal(["NameEditKeyPress"], inspector.EventMethods("OnKeyPress"));

        Select(designer, "NameEdit");
        Assert.Equal(["OnChange", "OnClick", "OnKeyPress"], inspector.EventRows.Select(r => r.Name));

        // DSG-OI-087: a class without events → the empty state.
        Select(designer, "MainForm");
        Assert.False(inspector.HasNoEvents);
        Assert.Equal(["OnClick", "OnCreate", "OnKeyPress"], inspector.EventRows.Select(r => r.Name));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-086")]
    public async Task MultiSelectionSharesHandler()
    {
        var (document, designer, inspector) = await OpenAsync();

        // Common events by name with identical signature; the value only when equal for all.
        Select(designer, "NameEdit", "GreetButton");
        Assert.Equal(["OnClick", "OnKeyPress"], inspector.EventRows.Select(r => r.Name));
        Assert.Equal("", Value(inspector, "OnClick"));

        // Double-click on the empty row: one shared handler named after the primary, assigned to all.
        Select(designer, "OkButton", "NameEdit");
        var count = designer.Undo.UndoCount;
        inspector.ActivateEvent("OnClick");
        Assert.Equal("NameEditClick", Stored(designer, "NameEdit", "OnClick"));
        Assert.Equal("NameEditClick", Stored(designer, "OkButton", "OnClick"));
        Assert.Equal(count + 1, designer.Undo.UndoCount);
        Assert.Single(document.Buffer.Text.Split("procedure TMainForm.NameEditClick(Sender: TObject);").Skip(1));

        // A new name with a common value V never renames V in a multi-selection: it creates and links.
        Assert.True(document.ShowView(DocumentView.Design));
        Select(designer, "OkButton", "NameEdit");
        Assert.Equal("NameEditClick", Value(inspector, "OnClick"));
        Assert.True(inspector.CommitEvent("OnClick", "BothClick"));
        Assert.Equal("BothClick", Stored(designer, "NameEdit", "OnClick"));
        Assert.Equal("BothClick", Stored(designer, "OkButton", "OnClick"));
        Assert.Contains("procedure TMainForm.NameEditClick(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("procedure TMainForm.BothClick(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);

        // Assigning an existing method to all.
        Select(designer, "OkButton", "NameEdit", "GreetButton");
        Assert.True(inspector.CommitEvent("OnClick", "SharedClick"));
        Assert.Equal(["SharedClick", "SharedClick", "SharedClick"],
            new[] { "OkButton", "NameEdit", "GreetButton" }.Select(n => Stored(designer, n, "OnClick")));
    }
}
