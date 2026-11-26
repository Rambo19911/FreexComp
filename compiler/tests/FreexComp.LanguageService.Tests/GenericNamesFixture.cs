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

    /// <summary>C-355: a unit whose generic nothing instantiates (the program only uses the unit).</summary>
    public static readonly string Uninstantiated = """
        unit UO;
        interface
        type
          TStack<T> = class
          public
            type
              TNode = class
              public
                Value: T;
                Next: TNode;
                function IsLast: Boolean;
              end;
          public
            Top: TNode;
            Count: Integer;
            procedure Push(const V: T);
            function Peek: T;
          end;
        implementation
        function TStack<T>.TNode.IsLast: Boolean;
        begin
          Result := Self.Next = nil;
        end;
        procedure TStack<T>.Push(const V: T);
        begin
          Self.Count := Count + 1;
        end;
        function TStack<T>.Peek: T;
        begin
          Result := Top.Value;
        end;
        end.
        """.ReplaceLineEndings("\n");

    /// <summary>C-355: the document of <c>UO.pas</c> in a project whose program uses <c>UO</c> without instantiating its generic.</summary>
    public static Document OpenUninstantiated(string? unitText = null) =>
        MarkupTestFile.Open("UO.pas", ("P.pas", "program P;\nuses UO;\nbegin\nend.\n"), ("UO.pas", unitText ?? Uninstantiated));

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
