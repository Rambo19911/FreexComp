using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>Object Pascal indentation rules beyond the data cases (testing.md §5): opener matching across lines.</summary>
[Trait("Phase", "1")]
public sealed class IndentationTests
{
    private const string Source = """
        procedure P;
        begin
          try
            case X of
              1: if A then
                B
              else
                C;
            else
              D := 'end';
            end;
          except
            repeat
              { end }
            until E;
          end;
        end;
        type
          TFoo = class(TObject)
            F: record
              G: Integer;
            end;
          end;
          TBar = class(TFoo);
          TMeta = class of TFoo;
          IBaz = interface
          end;
        procedure Q; assembler;
        asm
        end;
        """;

    /// <summary>The 1-based line of the opener matching the closer that starts <paramref name="line"/>.</summary>
    private static int? OpenerOf(ITextBuffer buffer, int line)
    {
        var text = EditorOperations.LineText(buffer, line);
        var indent = EditorOperations.Indentation(text).Length;
        var word = 0;
        while (indent + word < text.Length && char.IsLetter(text[indent + word]))
            word++;
        return new PascalIndentRules().MatchingOpenerLine(buffer, buffer.GetLine(line).Offset + indent + word);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-008")]
    public void EnterAndCloserRules()
    {
        var buffer = new StringTextBuffer(Source.ReplaceLineEndings("\n"));
        Assert.Equal(5, OpenerOf(buffer, 7)); // else of the if in a case arm
        Assert.Equal(4, OpenerOf(buffer, 9)); // else of the case: the if statement ended with ';'
        Assert.Equal(4, OpenerOf(buffer, 11)); // end of the case; 'end' in a string is no closer
        Assert.Equal(3, OpenerOf(buffer, 12)); // except of the try
        Assert.Equal(13, OpenerOf(buffer, 15)); // until of the repeat; 'end' in a comment is no closer
        Assert.Equal(3, OpenerOf(buffer, 16)); // end of try…except
        Assert.Equal(2, OpenerOf(buffer, 17)); // end of the routine body
        Assert.Equal(20, OpenerOf(buffer, 22)); // end of the nested record
        Assert.Equal(19, OpenerOf(buffer, 23)); // end of the class
        Assert.Equal(26, OpenerOf(buffer, 27)); // end of the interface type: the short forms above opened nothing
        Assert.Equal(29, OpenerOf(buffer, 30)); // end of asm
        Assert.Null(OpenerOf(buffer, 6)); // B is no closer
        Assert.Null(new PascalIndentRules().MatchingOpenerLine(new StringTextBuffer("end"), 3)); // no opener

        // IDE-ED-007: the lexer on the old line only, ignoring trailing comments.
        var rules = new PascalIndentRules();
        Assert.True(rules.IncreasesIndent("  for I := 1 to 3 do"));
        Assert.True(rules.IncreasesIndent("  end else // comment"));
        Assert.True(rules.IncreasesIndent("  'a', 'b':"));
        Assert.True(rules.IncreasesIndent("  published"));
        Assert.True(rules.IncreasesIndent("  finally"));
        Assert.True(rules.IncreasesIndent("initialization"));
        Assert.False(rules.IncreasesIndent("case X of 1: A := B"));
        Assert.False(rules.IncreasesIndent("  TMeta = class of TFoo"));
        Assert.False(rules.IncreasesIndent("  A: array of")); // 'of' without case
        Assert.False(rules.IncreasesIndent("  case X of 1: B := 2; 3:")); // the line's first token is 'case'
        Assert.False(rules.IncreasesIndent("  X := 1 { begin"));
        Assert.False(rules.IncreasesIndent(""));
    }
}
