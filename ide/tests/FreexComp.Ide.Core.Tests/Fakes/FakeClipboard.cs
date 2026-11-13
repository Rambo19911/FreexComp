using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Tests.Fakes;

/// <summary><see cref="IClipboardService"/> holding its text in memory.</summary>
public sealed class FakeClipboard : IClipboardService
{
    public string? Text { get; set; }

    public Task<string?> GetTextAsync() => Task.FromResult(Text);

    public Task SetTextAsync(string text)
    {
        Text = text;
        return Task.CompletedTask;
    }
}
