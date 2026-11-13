using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Text;

/// <summary>
/// Creates document buffers (platform service, IDE-ARCH-031): <c>AvaloniaEditTextBufferFactory</c> in the running
/// IDE, so the editor view binds to the buffer's document directly; <see cref="StringTextBufferFactory"/> in Core tests.
/// </summary>
public interface ITextBufferFactory
{
    /// <summary>A buffer holding <paramref name="text"/> with an empty undo history at the save point.</summary>
    ITextBuffer Create(string text, LineEndingKind defaultLineEnding);
}

/// <summary>Creates <see cref="StringTextBuffer"/>s.</summary>
public sealed class StringTextBufferFactory : ITextBufferFactory
{
    public ITextBuffer Create(string text, LineEndingKind defaultLineEnding) => new StringTextBuffer(text, defaultLineEnding);
}
