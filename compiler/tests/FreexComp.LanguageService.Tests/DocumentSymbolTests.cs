using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §7 CMP-743: the structure view, purely syntactic (C-140).</summary>
[Trait("Phase", "3")]
public sealed class DocumentSymbolTests
{
    private const string Path = @"C:\p\Unit1.pas";

    private static string Outline(string text)
    {
        var tree = SyntaxTree.Parse(SourceText.From(Path, text));
        var lines = new List<string>();
        void Walk(DocumentSymbol symbol, int depth)
        {
            Assert.True(symbol.Span.Start <= symbol.SelectionSpan.Start && symbol.SelectionSpan.End <= symbol.Span.End, symbol.Name);
            lines.Add(new string(' ', depth * 2) + symbol.Kind + " " + symbol.Name + (symbol.Detail.Length > 0 ? " : " + symbol.Detail : ""));
            foreach (var child in symbol.Children)
            {
                Walk(child, depth + 1);
            }
        }

        foreach (var symbol in NavigationService.GetDocumentSymbols(tree))
        {
            Walk(symbol, 0);
        }

        return string.Join("\n", lines);
    }

    [Fact]
    [Trait("Req", "CMP-743")]
    public void UnitWithClassesAndNestedRoutines()
    {
        const string text = """
            unit Unit1;
            interface
            uses SysUtils, Classes;
            type
              TColor = (Red, Green);
              TPoint = record X, Y: Integer; end;
              TFoo = class(TObject)
                FCount: Integer;
              private
                procedure SetCount(Value: Integer);
              public
                constructor Create;
                property Count: Integer read FCount write SetCount;
              end;
            const
              Max = 10;
            var
              Global: TFoo;
            function Twice(A: Integer): Integer;
            implementation
            uses Math;
            constructor TFoo.Create;
            begin
            end;
            procedure TFoo.SetCount(Value: Integer);
            begin
              FCount := Value;
            end;
            function Twice(A: Integer): Integer;
              function Inner: Integer;
                procedure Deepest;
                begin
                end;
              begin
                Result := A;
              end;
            begin
              Result := Inner * 2;
            end;
            end.
            """;

        Assert.Equal(
            """
            Unit Unit1
              Keyword interface
                Unit uses : SysUtils, Classes
                  Unit SysUtils
                  Unit Classes
                Enum TColor
                  EnumMember Red
                  EnumMember Green
                Type TPoint : record
                  Field X : Integer
                  Field Y : Integer
                Class TFoo : class(TObject)
                  Field FCount : Integer
                  Keyword private
                    Method SetCount : procedure SetCount(Value: Integer)
                  Keyword public
                    Method Create : constructor Create
                    Property Count : Integer
                Constant Max
                Variable Global : TFoo
                Routine Twice : function Twice(A: Integer): Integer
              Keyword implementation
                Unit uses : Math
                  Unit Math
                Method TFoo.Create : constructor TFoo.Create
                Method TFoo.SetCount : procedure TFoo.SetCount(Value: Integer)
                Routine Twice : function Twice(A: Integer): Integer
                  Routine Inner : function Inner: Integer
                    Routine Deepest : procedure Deepest
            """.ReplaceLineEndings("\n"),
            Outline(text.ReplaceLineEndings("\n")));
    }

    [Fact]
    [Trait("Req", "CMP-743")]
    public void ProgramOutline()
    {
        const string text = "program P;\nuses U1;\nvar I: Integer;\nprocedure Q; begin end;\nbegin\nend.";

        Assert.Equal(
            "Unit P\n  Unit uses : U1\n    Unit U1\n  Variable I : Integer\n  Routine Q : procedure Q",
            Outline(text));
    }

    [Fact]
    public void FileWithSyntaxErrors_DoesNotThrow()
    {
        var outline = Outline("unit U; interface type TFoo = class procedure ; end; implementation procedure X(; begin");

        Assert.StartsWith("Unit U", outline, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionSpanIsTheName()
    {
        const string text = "program P; procedure Q; begin end; begin end.";
        var tree = SyntaxTree.Parse(SourceText.From(Path, text));

        var q = Assert.Single(NavigationService.GetDocumentSymbols(tree)[0].Children);

        Assert.Equal(text.IndexOf('Q'), q.SelectionSpan.Start);
        Assert.Equal(1, q.SelectionSpan.Length);
        Assert.Equal(text.IndexOf("procedure", StringComparison.Ordinal), q.Span.Start);
        Assert.Equal(text.IndexOf("end;", StringComparison.Ordinal) + 4, q.Span.End);
    }
}
