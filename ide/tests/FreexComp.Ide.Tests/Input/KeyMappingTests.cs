using Avalonia.Input;
using FreexComp.Ide.Input;
using CoreGesture = FreexComp.Ide.Core.Commands.KeyGesture;

namespace FreexComp.Ide.Tests.Input;

[Trait("Phase", "1")]
public sealed class KeyMappingTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-001")]
    public void EveryGestureKeyRoundTrips()
    {
        foreach (var name in CoreGesture.KeyNames)
        {
            var gesture = CoreGesture.Parse("Ctrl+Shift+Alt+" + name);
            var (key, modifiers) = KeyMapping.ToAvalonia(gesture);

            Assert.Equal(gesture, KeyMapping.FromAvalonia(key, modifiers));
        }

        Assert.Equal((Key.S, KeyModifiers.Control), KeyMapping.ToAvalonia(CoreGesture.Parse("Ctrl+S")));
        Assert.Null(KeyMapping.FromAvalonia(Key.LeftCtrl, KeyModifiers.Control));
    }
}
