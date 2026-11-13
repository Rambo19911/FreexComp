using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Tests.Text;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Text;

namespace FreexComp.Ide.Tests.Text;

/// <summary><see cref="TextBufferContractTests"/> against the AvaloniaEdit-backed buffer (I-030).</summary>
public sealed class AvaloniaEditTextBufferContractTests : TextBufferContractTests
{
    protected override ITextBuffer CreateBuffer(string text, LineEndingKind defaultLineEnding = LineEndingKind.CrLf)
        => new AvaloniaEditTextBufferFactory().Create(text, defaultLineEnding);
}
