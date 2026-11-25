using FreexComp.MetadataTesting;

namespace FreexComp.UiFramework.Tests;

/// <summary>
/// U-138 (component-metadata.md §5, DSG-CS-055): the optional file key <c>eventSignatureUnits</c> of the design metadata names, for every
/// non-system type used in an <c>eventSignatures</c> text of that file, the unit whose interface really declares the type.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DesignMetadataTests
{
    [Fact]
    public void EventSignatureTypesHaveUnits()
    {
        var problems = MetadataDecls.EventSignatureTypesHaveUnits(MetadataRoot.Load(MetadataTests.DesignDirectory), MetadataTests.Source.Value);

        Assert.True(problems.Count == 0, MetadataMatch.Report(problems));
    }

    [Fact]
    public void SignatureTypesSkipSystemTypes()
    {
        var types = MetadataDecls.SignatureTypes("procedure(Sender: TObject; var Key: Word; const S: string; Shift: TShiftState; X, Y: Integer)");

        Assert.Equal(["TShiftState"], types);
    }

    /// <summary>U-151 (UIF-1378): <c>TAction</c> lists <c>SecondaryShortCuts</c> as a string-list property of the category Action.</summary>
    [Fact]
    [Trait("Req", "UIF-1378")]
    public void ActionHasSecondaryShortCutsRow()
    {
        var action = MetadataRoot.Load(MetadataTests.DesignDirectory).Find("TAction");

        Assert.NotNull(action);
        var row = Assert.Single(action.Properties, p => p.GetProperty("name").GetString() == "SecondaryShortCuts");
        Assert.Equal("TStrings", row.GetProperty("type").GetString());
        Assert.Equal("Action", row.GetProperty("category").GetString());
    }
}
