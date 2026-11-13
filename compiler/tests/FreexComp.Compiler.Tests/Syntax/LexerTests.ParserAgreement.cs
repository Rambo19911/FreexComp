using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// api.md CMP-222: <c>Lexer.Tokenize</c> and the parser's tree yield the same token kinds and spans. Holds for
/// error-free sources (the parser turns skipped tokens into trivia and inserts missing tokens); generics are not parsed
/// yet (C-019), so the inputs avoid them.
/// </summary>
public sealed partial class LexerTests
{
    private const string AgreementProgram = """
        program Demo;
        {$APPTYPE CONSOLE}
        uses Sys.Utils, Other in 'other.pas';
        label 10, Done;
        const
          N = 10; // count
          Names: array[0..1] of string = ('a', 'b');
          Origin: TPoint = (X: 0; Y: -1);
        type
          TPoint = record X, Y: Integer; end;
          PPoint = ^TPoint;
          TDigits = set of '0'..'9';
          TProc = procedure(const S: string) of object;
        var
          I, J: Integer;
          P: PPoint;
          S: string[20];
        {$IFDEF NEVER} junk that is not parsed {$ENDIF}
        function Max(A, B: Integer): Integer; inline;
        begin
          if A > B then Result := A else Result := B;
        end;

        procedure Show(const Msg: string; Width: Integer = 0); forward;

        procedure Show(const Msg: string; Width: Integer);
        var Local: Integer;
        begin
          WriteLn(Msg:Width, ' ', SizeOf(string), string(nil) = '');
          inherited Show(Msg);
        end;

        begin
          I := Max(1, 2) * -3 + Ord('A') div 2;
          P := @Origin; P^.X := P.Y shl 1;
          case I of
            1, 3..5: WriteLn('odd');
            -1: if I in [1, 2] then Exit;
          else
            goto Done;
          end;
          for I := N downto 0 do Continue;
          for var K: Integer := 0 to 9 do ;
          for var C in 'abc' do WriteLn(C);
          while not (I = 0) do Dec(I);
          repeat Inc(J) until (J >= 10) or (J < 0);
          with Origin do X := 1;
          try
            raise EError.Create('x') at nil;
          except
            on E: EError do WriteLn(E.Message);
            on Sys.EOther do ;
          else
            raise;
          end;
          try I := 1 finally J := 2 end;
          var Z := 1.5e3;
          const Q = #65'b';
        10:
        Done:
          WriteLn(Names[0], S[1], I <> $FF);
        end.
        """;

    private const string AgreementUnit = """
        unit Sys.Utils deprecated;
        interface
        uses A;
        type
          IShape = interface(IInterface) ['{8E2B1C3A-0000-4000-8000-000000000001}']
            function Area: Double;
          end;
          TShape = class abstract(TInterfacedObject, IShape)
          strict private
            FName: string;
            class var Count: Integer;
          protected
            procedure SetName(const Value: string); virtual;
          public
            constructor Create; overload;
            function Area: Double; virtual; abstract;
            function IShape.Area = Area;
            property Name: string read FName write SetName stored True default 0;
            property Items[I: Integer]: TShape read GetItem; default;
          end;
          TVec = record
          private
            FX: Single;
          public
            class operator Add(const A, B: TVec): TVec;
            const Zero = 0;
          end;
          TIntHelper = record helper for Integer
            function IsEven: Boolean; inline;
          end;
        procedure P(var X; out Y: Integer); stdcall;
        function F: Integer; external 'k.dll' name 'f';
        implementation
        procedure P(var X; out Y: Integer);
        begin
          Y := Integer(X) + &begin;
        end;
        initialization
          P(Q, R);
        finalization
          ;
        end.
        """;

    [Theory]
    [Trait("Req", "CMP-222")]
    [InlineData(AgreementProgram)]
    [InlineData(AgreementUnit)]
    public void TokenizeMatchesParserTokens(string text)
    {
        var source = SourceText.From(Path, text);

        var tree = SyntaxTree.Parse(source);

        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(
            Lexer.Tokenize(source).Select(t => (t.Kind, t.Span, t.FullSpan)),
            tree.Root.DescendantTokens().Where(t => !t.IsMissing).Select(t => (t.Kind, t.Span, t.FullSpan)));
    }
}
