using System.Runtime.InteropServices;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary><see cref="IClipboardService"/> holding its text in memory.</summary>
public sealed class FakeClipboard : IClipboardService
{
    public string? Text { get; set; }

    /// <summary>
    /// The next this many calls fail as when another program holds the clipboard open (an <see cref="ExternalException"/>,
    /// like the OS clipboard).
    /// </summary>
    public int FailNext { get; set; }

    /// <summary>Every call, failed ones included.</summary>
    public int Calls { get; private set; }

    public Task<string?> GetTextAsync()
    {
        Fail();
        return Task.FromResult(Text);
    }

    public Task SetTextAsync(string text)
    {
        Fail();
        Text = text;
        return Task.CompletedTask;
    }

    private void Fail()
    {
        Calls++;
        if (FailNext <= 0)
            return;
        FailNext--;
        throw new ExternalException("The clipboard is open in another program.");
    }
}
