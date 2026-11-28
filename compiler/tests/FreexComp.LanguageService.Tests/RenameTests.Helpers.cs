namespace FreexComp.LanguageService.Tests;

// CMP-1495 (C-446; phase8/helpers-completion.md §9, §15.9): renaming helper members.
public sealed partial class RenameTests
{
    private const string HelperUser = """
        unit U2;
        interface
        uses UH;
        procedure Call(D: TChild);
        implementation
        procedure Call(D: TChild);
        begin
          D.Show;
        end;
        end.
        """;

    private const string HelperProgram = """
        program P;
        uses UH, U2;
        type
          TGrand = class(TChild)
            procedure Go;
          end;
        procedure TGrand.Go;
        begin
          Show;
        end;
        var
          D: TChild;
        begin
          D.Show;
          with D do
            Show;
          D.Run;
          Call(D);
        end.
        """;

    private static (string Name, string Text)[] HelperFiles() =>
        [("P.pas", HelperProgram.ReplaceLineEndings("\n")), HelpersFixture.UnitFile, ("U2.pas", HelperUser.ReplaceLineEndings("\n"))];

    /// <summary>
    /// CMP-1495: renaming a helper member edits its declaration, its implementation heading and every reference bound to it — in the
    /// helper's unit, another unit and the program (a value, <c>with</c>, implicit <c>Self</c>) — and nothing of the helped type.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1495")]
    public void HelperMember_RenamesEveryReference()
    {
        var files = HelperFiles();
        AssertCompiles(files);

        var result = Rename(files, "UH.pas", "Show;", "Display");

        Assert.True(result.Success, result.Error);
        Assert.Equal(["P.pas: 3", "U2.pas: 1", "UH.pas: 3"], Counts(result));
        var after = Applied(files, result);
        AssertCompiles(after);
        Assert.Equal(TextOf(files, "P.pas").Replace("Show;", "Display;", StringComparison.Ordinal), TextOf(after, "P.pas"));
        Assert.Contains("procedure TCounterHelper.Display; begin Hidden end;", TextOf(after, "UH.pas"), StringComparison.Ordinal);
        Assert.Contains("procedure TCounter.Run; begin end;", TextOf(after, "UH.pas"), StringComparison.Ordinal);

        // the same from a reference through implicit Self in the program
        Assert.Equal(["P.pas: 3", "U2.pas: 1", "UH.pas: 3"], Counts(Rename(files, "P.pas", "Show;", "Display")));
    }

    /// <summary>
    /// CMP-1495, CMP-1506: a helper member renamed onto a member of the helped type that is used where the helper is active would take
    /// that use over — the rename is refused with the conflict at the use.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1495")]
    public void HelperMember_ConflictWithAMemberOfTheHelpedType()
    {
        var files = HelperFiles();
        AssertCompiles(files);

        var result = Rename(files, "UH.pas", "Show;", "Run");

        Assert.False(result.Success);
        Assert.Empty(result.Edits);
        Assert.Equal(["P.pas(17,5) Run [3]"], result.Conflicts.Select(Describe));
        Assert.Equal("Renaming 'Show' to 'Run' changes the meaning of 'Run' at P.pas(17,5).", result.Error);
    }
}
