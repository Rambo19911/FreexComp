using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>
/// C-164: the options fingerprint (CMP-150 rule 3: sorted defines, <c>-O</c>, <c>-g</c>, default directive states, target triple)
/// includes the unit alias table's content hash (LANG-533), so editing the table makes every unit out of date.
/// </summary>
[Trait("Phase", "2")]
[Trait("Req", "CMP-150")]
public sealed class FingerprintTests
{
    private static readonly CompilationOptions Base = new() { MainFile = @"C:\p\P.pas", Defines = ["B", "A"] };

    [Fact]
    public void SameInputs_SameFingerprint()
    {
        var a = Fingerprint.Options(Base, "sha256:00");
        Assert.StartsWith("sha256:", a, StringComparison.Ordinal);
        Assert.Equal(71, a.Length);
        Assert.Equal(a, Fingerprint.Options(Base with { Defines = ["A", "B"], MainFile = @"C:\q\Q.pas" }, "sha256:00"));
    }

    [Fact]
    [Trait("Req", "LANG-533")]
    public void AliasTableHash_IsPartOfTheFingerprint()
    {
        Assert.NotEqual(Fingerprint.Options(Base, "sha256:00"), Fingerprint.Options(Base, "sha256:01"));
        Assert.NotEqual(Fingerprint.Options(Base, ""), Fingerprint.Options(Base, "sha256:00"));
    }

    [Fact]
    public void CodeAffectingOptions_ArePartOfTheFingerprint()
    {
        var a = Fingerprint.Options(Base, "");
        Assert.NotEqual(a, Fingerprint.Options(Base with { Defines = ["A"] }, ""));
        Assert.NotEqual(a, Fingerprint.Options(Base with { Optimization = OptimizationLevel.O2 }, ""));
        Assert.NotEqual(a, Fingerprint.Options(Base with { DebugInfo = true }, ""));
        Assert.NotEqual(a, Fingerprint.Options(Base with { RangeChecks = true }, ""));
        Assert.NotEqual(a, Fingerprint.Options(Base with { OverflowChecks = true }, ""));
        Assert.NotEqual(a, Fingerprint.Options(Base with { Assertions = false }, ""));
    }

    /// <summary>The compilation reads <c>&lt;rtl&gt;\unit-aliases.json</c> for its fingerprint: editing the table changes it.</summary>
    [Fact]
    [Trait("Req", "LANG-533")]
    public void Compilation_FingerprintFollowsTheAliasTable()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", "begin end.");
        var options = Base with { RtlDirectory = @"C:\rtl" };
        var none = Compilation.Create(options, [], fs).OptionsFingerprint();
        fs.Add(@"C:\rtl\unit-aliases.json", """{ "aliases": { "A.B": "B" } }""");
        var one = Compilation.Create(options, [], fs).OptionsFingerprint();
        fs.Add(@"C:\rtl\unit-aliases.json", """{ "aliases": { "A.B": "B", "A.C": "C" } }""");
        var two = Compilation.Create(options, [], fs).OptionsFingerprint();
        Assert.Equal(none, Fingerprint.Options(options, ""));
        Assert.NotEqual(none, one);
        Assert.NotEqual(one, two);
    }
}
