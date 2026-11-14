using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.ViewModels;

[Trait("Phase", "1")]
[Trait("Kind", "Unit")]
public sealed class TypeAheadSearchTests
{
    private static readonly string[] Rows = ["Hello", "aaa", "Zed.pas", "lib", "MainForm.pas", "Missing.pas", "notes.md", "Unit1.pas", "Unit2.pas"];

    /// <summary>UI-A11Y-007: prefix type-ahead, case-insensitive, the prefix reset after a 1 s pause; a repeated character cycles.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-007")]
    public void PrefixSearchResetsAfterOneSecond()
    {
        var clock = new ManualClock();
        var search = new TypeAheadSearch(clock);

        Assert.Equal(4, search.Type("m", Rows, -1)); // MainForm.pas
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(5, search.Type("I", Rows, 4)); // "mi": Missing.pas
        Assert.Equal("mI", search.Prefix);
        Assert.Equal(-1, search.Type("x", Rows, 5)); // "mix": nothing, the selection stays

        clock.Advance(TypeAheadSearch.ResetAfter); // a pause: a new prefix
        Assert.Equal("", search.Prefix);
        Assert.Equal(6, search.Type("n", Rows, 5)); // notes.md

        clock.Advance(TypeAheadSearch.ResetAfter);
        Assert.Equal(7, search.Type("u", Rows, 6));
        Assert.Equal(8, search.Type("u", Rows, 7)); // "uu": the next row starting with u
        Assert.Equal(7, search.Type("u", Rows, 8)); // wraps around
        clock.Advance(TypeAheadSearch.ResetAfter);
        Assert.Equal(7, search.Type("unit1", Rows, 8)); // pasted text: the whole prefix from the current row on
        Assert.Equal(-1, new TypeAheadSearch(clock).Type("q", [], -1));
    }
}
