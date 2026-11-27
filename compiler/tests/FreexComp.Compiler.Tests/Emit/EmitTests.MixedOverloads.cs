using FreexComp.Compiler.Tests.Binding;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>C-419 (LANG-980 rule 6, CMP-1477 (3)): only the candidate a call chooses is instantiated for use.</summary>
public sealed partial class EmitTests
{
    /// <summary>
    /// The pair of spec §5.5 — a non-generic <c>Take(I: Integer)</c> and a generic <c>Take&lt;T&gt;</c> whose body does not compile for
    /// <c>Integer</c> — called with an Integer: the program compiles, and its module defines the non-generic method and no instance of
    /// the generic one. Called with an object (the control), the module defines that instance.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-980")]
    [Trait("Req", "CMP-1477")]
    public void UnchosenGenericCandidate_IsNotEmitted()
    {
        static string[] Definitions(string module) =>
            [.. module.Split('\n').Where(l => l.StartsWith("define ", StringComparison.Ordinal) && l.Contains("TProbe.Take", StringComparison.Ordinal))];

        var unchosen = EmitProgramIr("P", GenericBindingTests.ProbeProgram.Replace("{CALL}", "TProbe.Take(5)", StringComparison.Ordinal));
        var only = Assert.Single(Definitions(unchosen));
        Assert.DoesNotContain("Take<", only, StringComparison.Ordinal);
        Assert.DoesNotContain("TProbe.Take<", unchosen, StringComparison.Ordinal);

        var chosen = EmitProgramIr("P", GenericBindingTests.ProbeProgram.Replace("{CALL}", "TProbe.Take(O)", StringComparison.Ordinal));
        Assert.Contains(Definitions(chosen), d => d.Contains("TProbe.Take<System.TObject>", StringComparison.Ordinal));
    }
}
