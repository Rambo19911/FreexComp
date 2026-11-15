using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Tests.CodeModel;

/// <summary>Parameter info (I-116, IDE-ED-090) over the project <c>Compilation</c>.</summary>
[Trait("Phase", "3")]
public sealed class ParameterInfoTests
{
    private const string Unit =
        "unit Unit1;\ninterface\nimplementation\nprocedure Add(A: Integer); overload;\nbegin\nend;\n" +
        "procedure Add(A: Integer; B: string; C: Boolean); overload;\nbegin\nend;\nprocedure Run;\nbegin\n  \nend;\nend.\n";

    private static void Type(TextDocumentViewModel doc, string text)
    {
        foreach (var c in text)
            Assert.True(doc.TypeText(c.ToString()));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-090")]
    public async Task CurrentParameterTracksCommas()
    {
        var bed = await SemanticHighlightTests.StartAsync("");
        bed.File(BuildTestBed.UnitPath, Unit);
        var doc = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        bed.Services.Documents.Activate(doc);
        var info = Assert.IsType<ParameterInfoSession>(doc.ParameterInfo);
        var body = Unit.IndexOf("begin\n  \n", StringComparison.Ordinal) + "begin\n  ".Length;
        doc.Selection = TextSelection.At(body);

        // '(' after an identifier opens it at once; the overload with enough parameters for one argument comes first.
        Type(doc, "Add(");
        await info.Pending;
        Assert.True(info.IsOpen);
        Assert.Equal(2, info.Signatures.Count);
        Assert.Equal(0, info.ActiveParameter);
        Assert.Equal("A: Integer", info.CurrentParameter!.Label);
        Assert.Contains("Add(A: Integer)", info.SelectedSignature!.Label, StringComparison.Ordinal);
        Assert.Equal("1 of 2", info.CounterText);

        // Each top-level comma moves to the next parameter; the overload with enough parameters is pre-selected.
        Type(doc, "1,");
        await info.Pending;
        Assert.Equal(1, info.ActiveParameter);
        Assert.Equal("B: string", info.CurrentParameter!.Label);
        Assert.Equal("2 of 2", info.CounterText);
        Type(doc, " 'a,b',");
        await info.Pending;
        Assert.Equal(2, info.ActiveParameter); // the comma inside the string does not count
        Assert.Equal("C: Boolean", info.CurrentParameter!.Label);

        // The caret moving back into the first argument follows it.
        doc.Selection = TextSelection.At(body + "Add(".Length);
        await info.Pending;
        Assert.True(info.IsOpen);
        Assert.Equal(0, info.ActiveParameter);

        // Up/Down cycle the overloads (wrapping); Escape closes.
        var first = info.SelectedIndex;
        Assert.True(doc.HandleKey("Down"));
        Assert.Equal((first + 1) % 2, info.SelectedIndex);
        Assert.True(doc.HandleKey("Up"));
        Assert.Equal(first, info.SelectedIndex);
        Assert.True(doc.HandleKey("Escape"));
        Assert.False(info.IsOpen);
        Assert.False(doc.HandleKey("Down")); // closed: the key is not taken

        // Ctrl+Shift+Space reopens it inside the argument list.
        Assert.True(bed.Services.Commands.CanExecute(ParameterInfoController.CommandId));
        await bed.Services.Commands.ExecuteAsync(ParameterInfoController.CommandId);
        await info.Pending;
        Assert.True(info.IsOpen);

        // Typing the closing parenthesis at depth 0 (type-over of the auto-inserted one) closes it.
        var close = doc.Buffer.Text.IndexOf("',", StringComparison.Ordinal) + 2;
        doc.Selection = TextSelection.At(close);
        await info.Pending;
        Type(doc, " True)");
        await info.Pending;
        Assert.False(info.IsOpen);
        Assert.Contains("  Add(1, 'a,b', True)\n", doc.Buffer.Text, StringComparison.Ordinal);

        // ',' inside an argument list opens it when it is not open.
        doc.Selection = TextSelection.At(doc.Buffer.Text.IndexOf("1,", StringComparison.Ordinal) + 1);
        await info.Pending;
        Type(doc, ",");
        await info.Pending;
        Assert.True(info.IsOpen);
        Assert.Equal(1, info.ActiveParameter);

        // The caret leaving the argument list closes it.
        doc.Selection = TextSelection.At(body);
        await info.Pending;
        Assert.False(info.IsOpen);

        // With the setting off, '(' does not open it.
        bed.Services.Settings.Set(SettingsCatalog.EditorParameterInfoAutoTrigger, false);
        doc.Selection = TextSelection.At(doc.Buffer.Text.IndexOf("True)", StringComparison.Ordinal) + 5);
        Type(doc, ";\n  Add(");
        await info.Pending;
        Assert.False(info.IsOpen);
    }
}
