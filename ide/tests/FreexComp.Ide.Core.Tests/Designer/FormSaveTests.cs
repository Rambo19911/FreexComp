using System.Text;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The bytes of a saved <c>.form</c> (DSG-FD-040) and the all-or-nothing save of a form document (I-220).</summary>
[Trait("Phase", "5")]
public sealed class FormSaveTests
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static void Edit(FormDesignerViewModel designer, FormChange change)
    {
        var inverse = designer.Editor!.Apply([change]);
        designer.Undo.Push(new DesignerUndoUnit("Undo_Designer_Property", [change], inverse, [], SelectionSnapshot.Empty, SelectionSnapshot.Empty));
    }

    private static async Task<FormDocumentViewModel> OpenAsync(DesignerFixture f, byte[] unit, byte[] form)
    {
        f.Memory.CreateDirectory(DesignerFixture.Dir);
        f.Memory.WriteAllBytes(DesignerFixture.UnitPath, unit);
        f.Memory.WriteAllBytes(DesignerFixture.FormPath, form);
        var result = await f.Services.Documents.OpenAsync(DesignerFixture.UnitPath);
        Assert.True(result.Success, result.MessageId);
        var document = Assert.IsType<FormDocumentViewModel>(result.Value);
        Assert.True(document.ShowView(DocumentView.Design));
        return document;
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-040")]
    public Task AlwaysUtf8WithoutBom() => TestUiThread.RunAsync(AlwaysUtf8WithoutBomOnUiThread);

    private static async Task AlwaysUtf8WithoutBomOnUiThread()
    {
        // A form read with a BOM is written without one.
        var f = new DesignerFixture();
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var document = await OpenAsync(f, Utf8NoBom.GetBytes(DesignerFixture.Unit), [.. bom, .. Utf8NoBom.GetBytes(DesignerFixture.Form)]);
        Edit(document.Designer, new SetProperty("MainForm", "Caption", new StringValue("Hello")));
        Assert.True(await f.Services.Documents.SaveAsync(document));
        var saved = f.Memory.ReadAllBytes(DesignerFixture.FormPath);
        Assert.False(saved.AsSpan().StartsWith(bom));
        Assert.Contains("  Caption = 'Hello'\n", Utf8NoBom.GetString(saved), StringComparison.Ordinal);

        // A form read as Windows-1252 (not valid UTF-8) takes a caption outside that code page and is written as UTF-8.
        f = new DesignerFixture();
        var form1252 = FileEncodings.Encode(DesignerFixture.Form.Replace("'MainForm'", "'Café'", StringComparison.Ordinal), FileEncoding.Windows1252);
        document = await OpenAsync(f, Utf8NoBom.GetBytes(DesignerFixture.Unit), form1252);
        Assert.Equal(FileEncoding.Windows1252, document.FormText.Encoding);
        Edit(document.Designer, new SetProperty("MainForm", "Caption", new StringValue("Café Ω")));
        Assert.True(await f.Services.Documents.SaveAsync(document));
        saved = f.Memory.ReadAllBytes(DesignerFixture.FormPath);
        Assert.False(saved.AsSpan().StartsWith(bom));
        Assert.Equal(FormWriter.Write(document.Designer.Model!), Utf8NoBom.GetString(saved));
        Assert.False(document.IsModified);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-040")]
    public Task UnitEncodingFailureWritesNeitherFile() => TestUiThread.RunAsync(UnitEncodingFailureWritesNeitherFileOnUiThread);

    private static async Task UnitEncodingFailureWritesNeitherFileOnUiThread()
    {
        // The unit is Windows-1252 and gets a character outside it; the form is modified too: nothing is written.
        var f = new DesignerFixture();
        var unit1252 = FileEncodings.Encode(DesignerFixture.Unit.Replace("unit MainForm;", "unit MainForm; { Café }", StringComparison.Ordinal), FileEncoding.Windows1252);
        var document = await OpenAsync(f, unit1252, Utf8NoBom.GetBytes(DesignerFixture.Form));
        Assert.Equal(FileEncoding.Windows1252, document.Encoding);
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "{ Ω }\r\n")]);
        Edit(document.Designer, new SetProperty("MainForm", "Caption", new StringValue("Hello")));

        var result = await f.Services.Documents.SaveAsAsync(document, DesignerFixture.UnitPath);
        Assert.False(result.Success);
        Assert.Equal("Msg.File.EncodingLossy", result.MessageId);
        Assert.Equal(unit1252, f.Memory.ReadAllBytes(DesignerFixture.UnitPath));
        Assert.Equal(DesignerFixture.Form, Utf8NoBom.GetString(f.Memory.ReadAllBytes(DesignerFixture.FormPath)));
        Assert.True(document.IsModified);
    }
}
