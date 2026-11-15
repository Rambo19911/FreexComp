using System.Text;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Text;

/// <summary>IDE-ED-024 block completion (I-120): Enter after an opener without closer, through the document's key handling.</summary>
[Trait("Phase", "3")]
public sealed class BlockCompletionTests
{
    private const string Head = "unit U;\r\ninterface\r\nimplementation\r\n";

    /// <summary>Opens <paramref name="before"/> (caret at <c>|</c>), presses Enter, returns the text with the caret as <c>|</c>.</summary>
    private static async Task<(string Text, TextDocumentViewModel Doc)> EnterAsync(string before, bool enabled = true)
    {
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        services.Settings.Set(SettingsCatalog.EditorAutoCloseBeginEnd, enabled);
        fs.CreateDirectory(@"C:\t");
        var caret = before.IndexOf('|', StringComparison.Ordinal);
        fs.WriteAllBytes(@"C:\t\U.pas", Encoding.UTF8.GetBytes(before.Remove(caret, 1)));
        var opened = await services.Documents.OpenAsync(@"C:\t\U.pas");
        var doc = Assert.IsType<TextDocumentViewModel>(opened.Value);
        doc.Selection = TextSelection.At(caret);
        Assert.True(doc.HandleKey("Enter"));
        return (doc.Buffer.Text.Insert(doc.Selection.Caret, "|"), doc);
    }

    [Theory(Timeout = 30000)]
    [Trait("Req", "IDE-ED-024")]
    // begin without end: end; at the opener line's indentation, the caret on the indented middle line
    [InlineData("procedure P;\r\nbegin\r\n  if True then begin|\r\nend;\r\nend.\r\n",
        "procedure P;\r\nbegin\r\n  if True then begin\r\n    |\r\n  end;\r\nend;\r\nend.\r\n")]
    // try: finally / end;
    [InlineData("procedure P;\r\nbegin\r\n  try|\r\nend;\r\nend.\r\n",
        "procedure P;\r\nbegin\r\n  try\r\n    |\r\n  finally\r\n  end;\r\nend;\r\nend.\r\n")]
    // repeat: until ;
    [InlineData("procedure P;\r\nbegin\r\n  repeat|\r\nend;\r\nend.\r\n",
        "procedure P;\r\nbegin\r\n  repeat\r\n    |\r\n  until ;\r\nend;\r\nend.\r\n")]
    // case … of
    [InlineData("procedure P;\r\nbegin\r\n  case 1 of|\r\nend;\r\nend.\r\n",
        "procedure P;\r\nbegin\r\n  case 1 of\r\n    |\r\n  end;\r\nend;\r\nend.\r\n")]
    // record and class headings
    [InlineData("type\r\n  TR = record|\r\nend.\r\n", "type\r\n  TR = record\r\n    |\r\n  end;\r\nend.\r\n")]
    [InlineData("type\r\n  TC = class(TObject)|\r\nend.\r\n", "type\r\n  TC = class(TObject)\r\n    |\r\n  end;\r\nend.\r\n")]
    // the opener already has its closer: a plain line break
    [InlineData("procedure P;\r\nbegin|\r\nend;\r\nend.\r\n", "procedure P;\r\nbegin\r\n  |\r\nend;\r\nend.\r\n")]
    // text right of the caret: no opener at the end of the old line
    [InlineData("procedure P;\r\nbegin\r\n  if True then begin| X;\r\nend;\r\nend.\r\n",
        "procedure P;\r\nbegin\r\n  if True then begin\r\n    | X;\r\nend;\r\nend.\r\n")]
    public async Task InsertsMatchingCloser(string before, string after)
    {
        var (text, doc) = await EnterAsync(Head + before);
        Assert.Equal(Head + after, text);

        // One undo step together with the line break.
        doc.Buffer.Undo();
        Assert.Equal((Head + before).Replace("|", "", StringComparison.Ordinal), doc.Buffer.Text);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-024")]
    public async Task EndWithoutSemicolonBeforeElseAndOffWhenDisabled()
    {
        var (text, _) = await EnterAsync(Head + "procedure P;\r\nbegin\r\n  if True then begin|\r\n  else\r\n    P;\r\nend;\r\nend.\r\n");
        Assert.Equal(Head + "procedure P;\r\nbegin\r\n  if True then begin\r\n    |\r\n  end\r\n  else\r\n    P;\r\nend;\r\nend.\r\n", text);

        (text, _) = await EnterAsync(Head + "procedure P;\r\nbegin\r\n  if True then begin|\r\nend;\r\nend.\r\n", enabled: false);
        Assert.Equal(Head + "procedure P;\r\nbegin\r\n  if True then begin\r\n    |\r\nend;\r\nend.\r\n", text);
    }
}
