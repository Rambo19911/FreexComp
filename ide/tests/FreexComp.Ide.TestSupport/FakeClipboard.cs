using System.Runtime.InteropServices;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary><see cref="IClipboardService"/> holding its text and extra formats in memory.</summary>
public sealed class FakeClipboard : IClipboardService
{
    private string? _text;

    /// <summary>The clipboard text; setting it is another program's copy (the extra formats are gone).</summary>
    public string? Text
    {
        get => _text;
        set
        {
            _text = value;
            Formats.Clear();
        }
    }

    /// <summary>The extra string formats put with the text (<see cref="IClipboardService.LineFormat"/>).</summary>
    public Dictionary<string, string> Formats { get; } = new(StringComparer.Ordinal);

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

    public Task SetTextAsync(string text, IReadOnlyDictionary<string, string> formats)
    {
        Fail();
        Text = text;
        foreach (var (name, value) in formats)
            Formats[name] = value;
        return Task.CompletedTask;
    }

    public Task<string?> GetFormatAsync(string format)
    {
        Fail();
        return Task.FromResult(Formats.TryGetValue(format, out var value) ? value : null);
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
