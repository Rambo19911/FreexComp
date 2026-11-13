using FreexComp.Ide.Core.Commands;

namespace FreexComp.Ide.Core.Tests.Commands;

[Trait("Phase", "1")]
public sealed class KeyGestureTests
{
    [Theory(Timeout = 30000)]
    [Trait("Req", "UI-KEY-001")]
    [InlineData("Ctrl+S", "Ctrl+S")]
    [InlineData("ctrl+shift+s", "Ctrl+Shift+S")]
    [InlineData("Shift+Ctrl+F5", "Ctrl+Shift+F5")]
    [InlineData("alt+shift+enter", "Shift+Alt+Enter")]
    [InlineData("Ctrl+Alt+Break", "Ctrl+Alt+Break")]
    [InlineData("F12", "F12")]
    [InlineData("pageup", "PageUp")]
    [InlineData("CTRL+PAGEDOWN", "Ctrl+PageDown")]
    [InlineData("Alt+Multiply", "Alt+Multiply")]
    [InlineData("Ctrl+/", "Ctrl+/")]
    [InlineData("Ctrl+Shift+-", "Ctrl+Shift+-")]
    [InlineData("Ctrl+=", "Ctrl+=")]
    [InlineData("Ctrl+,", "Ctrl+,")]
    [InlineData("Ctrl+]", "Ctrl+]")]
    [InlineData("Ctrl+Shift+[", "Ctrl+Shift+[")]
    [InlineData("Ctrl+`", "Ctrl+`")]
    [InlineData("Ctrl+\\", "Ctrl+\\")]
    [InlineData("Ctrl+'", "Ctrl+'")]
    [InlineData("Ctrl+;", "Ctrl+;")]
    [InlineData("Ctrl+.", "Ctrl+.")]
    [InlineData("Alt+0", "Alt+0")]
    [InlineData(" Ctrl + Space ", "Ctrl+Space")]
    public void ParseAndFormatRoundTrip(string text, string canonical)
    {
        var gesture = KeyGesture.Parse(text);

        Assert.Equal(canonical, gesture.ToString());
        Assert.Equal(gesture, KeyGesture.Parse(canonical));
        Assert.Equal(gesture, KeyGesture.Parse(gesture.ToString().ToLowerInvariant()));
    }

    [Theory(Timeout = 30000)]
    [Trait("Req", "UI-KEY-001")]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Alt")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Foo")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Win+A")]
    [InlineData("F13")]
    [InlineData("Ctrl+K, Ctrl+C")]
    public void InvalidTextIsRejected(string text)
    {
        Assert.False(KeyGesture.TryParse(text, out _));
        Assert.Throws<FormatException>(() => KeyGesture.Parse(text));
    }

    [Fact(Timeout = 30000)]
    public void EveryDefaultGestureIsCanonical()
    {
        Assert.All(DefaultKeymap.Bindings, b => Assert.Equal(b.Gesture, KeyGesture.Parse(b.Gesture.ToString())));
        Assert.All(CommandCatalog.All.SelectMany(c => c.DefaultGestures), g => Assert.Equal(g, KeyGesture.Parse(g).ToString()));
    }
}
