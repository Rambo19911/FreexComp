namespace FreexComp.LanguageService.Tests;

/// <summary>
/// generics.md CMP-1394 (C-347): a unit with nested classes, a generic nested in a class and a generic with nested types, and a
/// program naming them qualified (unit, type and instance qualifiers).
/// </summary>
internal static class GenericNamesFixture
{
    public static readonly string Unit = """
        unit UG;
        interface
        type
          TOuter = class
          public
            type
              TInner = class
              public
                procedure Go;
              end;
              TBox<T> = class
              public
                Value: T;
              end;
          end;
          TList<T> = class
          public
            type
              TEnumerator = class
              public
                function MoveNext: Boolean;
              end;
          public
            Items: Integer;
            class var Count: Integer;
            class function Make: TList<T>;
            procedure Add(const V: T);
          end;
        implementation
        procedure TOuter.TInner.Go;
        begin
        end;
        function TList<T>.TEnumerator.MoveNext: Boolean;
        begin
          Result := False;
        end;
        class function TList<T>.Make: TList<T>;
        begin
          Result := nil;
        end;
        procedure TList<T>.Add(const V: T);
        begin
        end;
        end.
        """.ReplaceLineEndings("\n");

    public static readonly string Program = """
        program P;
        uses UG;
        var
          B: UG.TOuter.TBox<Integer>;
          E: TList<Integer>.TEnumerator;
          Q: UG.TList<string>.TEnumerator;
          I: TOuter.TInner;
        begin
          E := TList<Integer>.TEnumerator.Create;
          B := TOuter.TBox<Integer>.Create;
          Writeln(E.MoveNext, TList<Integer>.Count);
        end.
        """.ReplaceLineEndings("\n");

    /// <summary>The project document of <paramref name="path"/> (<c>P.pas</c> or <c>UG.pas</c>).</summary>
    public static Document Open(string path = "P.pas") => MarkupTestFile.Open(path, ("P.pas", Program), ("UG.pas", Unit));

    /// <summary>The offset of the <paramref name="occurrence"/>-th <paramref name="piece"/> in <paramref name="text"/>.</summary>
    public static int Offset(string text, string piece, int occurrence = 1)
    {
        var at = -1;
        for (var i = 0; i < occurrence; i++)
        {
            at = text.IndexOf(piece, at + 1, StringComparison.Ordinal);
            Assert.True(at >= 0, piece);
        }

        return at;
    }
}
