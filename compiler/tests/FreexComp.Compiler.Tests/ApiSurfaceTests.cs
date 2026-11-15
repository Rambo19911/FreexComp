using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.Tests;

/// <summary>testing.md §3 (C-150): the public API of FreexComp.Compiler equals the checked-in <c>compiler/src/FreexComp.Compiler/PublicAPI.txt</c>.</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
[Trait("Req", "CMP-240")]
public sealed class ApiSurfaceTests
{
    [Fact]
    public void PublicApi_EqualsPublicApiTxt() =>
        PublicApiListing.AssertMatches(typeof(FreexComp.Compiler.Compilation).Assembly, Path.Combine("compiler", "src", "FreexComp.Compiler", "PublicAPI.txt"));
}
