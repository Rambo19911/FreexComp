namespace FreexComp.LanguageService.Tests;

/// <summary>
/// generics.md CMP-1400 (C-377): a unit whose class <c>TA</c> has a class constructor <c>Create</c> next to an ordinary
/// <c>constructor Create(N)</c> and a class destructor <c>Teardown</c>, a nested class <c>TA.TN</c> with a class destructor
/// <c>Done</c> and a record <c>TR</c> with a class constructor <c>Init</c>, a generic
/// <c>TBox&lt;T&gt;</c> with a class constructor <c>Create</c>; and a program using them.
/// </summary>
internal static class ClassConstructorFixture
{
    public static readonly string Unit = """
        unit UC;
        interface
        type
          TA = class
          public
            class var Value: Integer;
            class constructor Create;
            class destructor Teardown;
            constructor Create(N: Integer);
            procedure Run;
          public
            type
              TN = class
              public
                class destructor Done;
              end;
          end;
          TR = record
            class constructor Init;
          end;
          TBox<T> = class
            class constructor Create;
          end;
        implementation
        class constructor TA.Create;
        begin
          Value := 42;
        end;
        class destructor TA.Teardown;
        begin
        end;
        constructor TA.Create(N: Integer);
        begin
        end;
        procedure TA.Run;
        begin
        end;
        class destructor TA.TN.Done;
        begin
        end;
        class constructor TR.Init;
        begin
        end;
        class constructor TBox<T>.Create;
        begin
        end;
        end.
        """.ReplaceLineEndings("\n");

    public static readonly string Program = """
        program P;
        uses UC;
        var A: TA;
        begin
          A := TA.Create(1);
        end.
        """.ReplaceLineEndings("\n");

    /// <summary>The offset of the <paramref name="occurrence"/>-th <paramref name="piece"/> in <paramref name="text"/>.</summary>
    public static int IndexOf(string text, string piece, int occurrence = 1)
    {
        var at = -1;
        for (var i = 0; i < occurrence; i++)
        {
            at = text.IndexOf(piece, at + 1, StringComparison.Ordinal);
        }

        Assert.True(at >= 0, piece);
        return at;
    }
}
