using System.Text;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// C-250 (LANG-182/183): error recovery stays local. A half-typed line in one routine of a large unit makes the parser
/// skip only tokens near it: it resynchronises at statement and declaration boundaries (<c>;</c>, <c>begin</c>/<c>end</c>,
/// section keywords, routine headings) instead of skipping the rest of the file.
/// </summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
[Trait("Req", "LANG-182")]
[Trait("Req", "LANG-183")]
public sealed class ParserRecoveryTests(ITestOutputHelper output)
{
    private const int Routines = 225;

    /// <summary>
    /// The bound on tokens skipped per syntax error: at most the rest of the edited routine body (the fixture's body has
    /// 61 tokens after the edit point), never the rest of the file (before C-250: 26 571 tokens after a stray <c>end</c>).
    /// </summary>
    private const int SkippedPerErrorBound = 64;

    /// <summary>Lines in the middle of being typed, one per edited routine (the rest of the routine is complete).</summary>
    public static readonly TheoryData<string> HalfTypedLines =
    [
        "  Sum := Sum +",
        "  Sum := Sum + Item.",
        "  Sum := (Sum",
        "  Sum := Item.Twice(Sum",
        "  Writeln(T",
        "  Writeln(T, [1, 2",
        "  if I >",
        "  if Sum > 0 then",
        "  if Sum > 0 then begin",
        "  for I := 1 to",
        "  for I := 1 to A do begin",
        "  while Sum",
        "  case Sum of",
        "  case Sum of 1:",
        "  repeat",
        "  try",
        "  try Sum := 1; except",
        "  with Item do",
        "  begin",
        "  T := 'abc",
        "  T := S[",
        "  var X: ",
        "  var X := ",
        "  Item := TItem.",
        "  inherited",
        "  raise",
        "  asm",
        "  Sum := Sum ) + 1;",
        "  ]",
        "  procedure",
        "  type",
        "  else",
        "  until",
        "  end",
        "  of",
        "  :=",
    ];

    /// <summary>A unit of 225 routines (~5 900 lines) with <paramref name="line"/> inserted in every 25th routine body.</summary>
    internal static string HalfTypedUnit(string line, int routines = Routines)
    {
        var sb = new StringBuilder();
        sb.Append("unit Big;\n\ninterface\n\ntype\n  TItem = class\n  public\n    Value: Integer;\n    Name: string;\n")
          .Append("    function Twice(A: Integer): Integer;\n  end;\n\n");
        for (var i = 0; i < routines; i++)
        {
            sb.Append("function Work").Append(i).Append("(A: Integer; const S: string): Integer;\n");
        }

        sb.Append("\nimplementation\n\nfunction TItem.Twice(A: Integer): Integer;\nbegin\n  Result := A * 2;\nend;\n");
        for (var i = 0; i < routines; i++)
        {
            var edit = i % 25 == 12 ? line + "\n" : "";
            sb.Append($$"""

                function Work{{i}}(A: Integer; const S: string): Integer;
                var
                  I, Sum: Integer;
                  T: string;
                  Item: TItem;
                begin
                  Sum := 0;
                  for I := 1 to A do
                  begin
                    if I mod 3 = 0 then
                      Sum := Sum + I
                    else
                      Sum := Sum - 1;
                  end;
                {{edit}}  T := S + 'x';
                  if Length(T) > 2 then
                    Sum := Sum + Length(T);
                  while Sum > 100 do
                    Sum := Sum div 2;
                  Item := TItem.Create;
                  Sum := Sum + Item.Twice(Sum);
                  Item.Free;
                  Result := Sum;
                end;

                """.ReplaceLineEndings("\n"));
        }

        sb.Append("\nend.\n");
        return sb.ToString();
    }

    /// <summary>Every token the parser skipped (one <c>SkippedTokensTrivia</c> per token, LANG-182).</summary>
    internal static int SkippedTokens(SyntaxTree tree) =>
        tree.Root.DescendantTokens().Sum(t => t.LeadingTrivia.Count(x => x.Kind == SyntaxKind.SkippedTokensTrivia)
                                              + t.TrailingTrivia.Count(x => x.Kind == SyntaxKind.SkippedTokensTrivia));

    /// <summary>
    /// A stray <c>end ;</c> in a unit section is one E1100 and the section goes on (before C-250: E1101 "'.' expected but
    /// ';' found", then H1001 and the rest of the file skipped); <c>end ;</c> at the end of the file still ends the unit.
    /// </summary>
    [Theory]
    [InlineData("unit U; interface implementation procedure P; begin end; end; procedure Q; begin end; end.",
        "E1100(1,58): Syntax error: unexpected 'end'")]
    [InlineData("unit U; interface end; procedure Q; implementation procedure Q; begin end; end.",
        "E1100(1,19): Syntax error: unexpected 'end'")]
    [InlineData("unit U; interface implementation procedure P; begin end; end;",
        "E1101(1,61): '.' expected but ';' found")]
    public void StrayEndInAUnitSection_IsSkipped(string text, string expected)
    {
        var tree = SyntaxTree.Parse(SourceText.From(@"C:\p\U.pas", text));

        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Equal([expected], tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Code}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Message}"));
    }

    [Theory]
    [MemberData(nameof(HalfTypedLines))]
    public void HalfTypedLinesSkipLocally(string line)
    {
        var text = HalfTypedUnit(line);
        var tree = SyntaxTree.Parse(SourceText.From(@"C:\p\Big.pas", text));

        var errors = tree.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var skipped = SkippedTokens(tree);
        output.WriteLine($"'{line}': {errors} errors, {skipped} skipped tokens");

        Assert.Equal(text, tree.Root.ToFullString());
        Assert.InRange(errors, 0, 100);                                   // some lines are already complete statements
        Assert.True(skipped <= SkippedPerErrorBound * Math.Max(errors, 1), $"'{line}': {skipped} tokens skipped for {errors} errors");

        // every routine after an edit is still parsed as a routine of the implementation section
        var routines = ((UnitSyntax)tree.Root).ImplementationSection.Declarations.OfType<RoutineDeclarationSyntax>()
            .Select(r => r.Heading.Name.ToString()).Where(n => n.StartsWith("Work", StringComparison.Ordinal)).Distinct().Count();
        Assert.Equal(Routines, routines);
    }
}
