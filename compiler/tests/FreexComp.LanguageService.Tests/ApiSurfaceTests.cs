using FreexComp.Tests.Shared;

namespace FreexComp.LanguageService.Tests;

/// <summary>testing.md §3 (C-150): the public API of FreexComp.LanguageService equals the checked-in <c>compiler/src/FreexComp.LanguageService/PublicAPI.txt</c>.</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
[Trait("Req", "CMP-240")]
public sealed class ApiSurfaceTests
{
    [Fact]
    public void PublicApi_EqualsPublicApiTxt() =>
        PublicApiListing.AssertMatches(typeof(FreexComp.LanguageService.Workspace).Assembly, Path.Combine("compiler", "src", "FreexComp.LanguageService", "PublicAPI.txt"));
}
