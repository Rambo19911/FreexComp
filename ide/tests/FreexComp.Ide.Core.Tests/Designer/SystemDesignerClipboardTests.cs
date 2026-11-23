using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The designer clipboard over the OS clipboard (design-surface.md DSG-SUR-110, 120, 125; D-078).</summary>
[Trait("Phase", "5")]
public sealed class SystemDesignerClipboardTests
{
    [Fact]
    [Trait("Req", "DSG-SUR-110")]
    [Trait("Req", "DSG-SUR-125")]
    public Task WritesThroughAndReadsOtherProgramsCopies() => TestUiThread.RunAsync(() => WritesThroughAndReadsOtherProgramsCopiesOnUiThread()); // I-209

    private async Task WritesThroughAndReadsOtherProgramsCopiesOnUiThread()
    {
        var os = new FakeClipboard();
        var clipboard = new SystemDesignerClipboard(os);
        Assert.Null(clipboard.GetData(ClipboardFormats.Text));

        // A copy: both formats at once, the plain text as the clipboard text; a new sequence number.
        clipboard.SetData(new Dictionary<string, string> { [ClipboardFormats.Components] = "; parent-origin 17 47\nobject A: TButton\nend\n", [ClipboardFormats.Text] = "object A: TButton\nend\n" });
        Assert.True(await clipboard.FlushAsync());
        Assert.Equal("object A: TButton\nend\n", os.Text);
        Assert.Equal("; parent-origin 17 47\nobject A: TButton\nend\n", os.Formats[ClipboardFormats.Components]);
        var afterCopy = clipboard.SequenceNumber;

        // Reading back our own content changes nothing.
        await clipboard.RefreshAsync();
        Assert.Equal(afterCopy, clipboard.SequenceNumber);

        // Another program's copy replaces it (only plain text now), with a new sequence number.
        os.Text = "object B: TLabel\nend\n";
        await clipboard.RefreshAsync();
        Assert.NotEqual(afterCopy, clipboard.SequenceNumber);
        Assert.Equal("object B: TLabel\nend\n", clipboard.GetData(ClipboardFormats.Text));
        Assert.Null(clipboard.GetData(ClipboardFormats.Components));

        // A busy clipboard: a refused read keeps the known content, a refused write is reported by FlushAsync.
        var known = clipboard.SequenceNumber;
        os.FailNext = 1;
        os.Text = "something else";
        await clipboard.RefreshAsync();
        Assert.Equal(known, clipboard.SequenceNumber);
        Assert.Equal("object B: TLabel\nend\n", clipboard.GetData(ClipboardFormats.Text));
        os.FailNext = 1;
        clipboard.SetData(new Dictionary<string, string> { [ClipboardFormats.Text] = "x" });
        Assert.False(await clipboard.FlushAsync());
        Assert.Equal("x", clipboard.GetData(ClipboardFormats.Text)); // the designer keeps its copy for its own paste
    }
}
