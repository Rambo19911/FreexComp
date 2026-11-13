using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Text;

/// <summary><see cref="TextBufferContractTests"/> against the Core implementation (I-029).</summary>
public sealed class StringTextBufferContractTests : TextBufferContractTests
{
    protected override ITextBuffer CreateBuffer(string text, LineEndingKind defaultLineEnding = LineEndingKind.CrLf)
        => new StringTextBuffer(text, defaultLineEnding);
}
