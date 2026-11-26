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

    /// <summary>
    /// C-358: a generic nothing instantiates whose type parameters have a class, an interface and a <c>constructor</c> constraint, and
    /// whose method calls a generic method with its own type parameter.
    /// </summary>
    public static readonly string Constrained = """
        unit UO;
        interface
        type
          TThing = class
          public
            Name: string;
            constructor Create;
            procedure Rename(const S: string);
          end;
          IShape = interface
            function Area: Integer;
          end;
          TUtil = class
          public
            class function Max<T>(const A, B: T): T;
          end;
          THolder<T: TThing; S: IShape; C: constructor> = class
          public
            Item: T;
            Shape: S;
            function Make: C;
            function Larger(const A, B: T): T;
            procedure Use;
          end;
        implementation
        constructor TThing.Create;
        begin
        end;
        procedure TThing.Rename(const S: string);
        begin
          Name := S;
        end;
        class function TUtil.Max<T>(const A, B: T): T;
        begin
          Result := A;
        end;
        function THolder<T, S, C>.Make: C;
        begin
          Result := C.Create;
        end;
        function THolder<T, S, C>.Larger(const A, B: T): T;
        begin
          Result := TUtil.Max<T>(A, B);
        end;
        procedure THolder<T, S, C>.Use;
        begin
          if Shape.Area > 0 then
            Item.Rename(Item.Name);
        end;
        end.
        """.ReplaceLineEndings("\n");

    /// <summary>
    /// C-364: a generic nothing instantiates whose method bodies use members of members of constrained type parameters
    /// (<c>Item.Owner.Rename</c>, <c>Shape.Bounds.Title</c>) and call generic methods with inferred type arguments, qualified
    /// (<c>TUtil.Max(A, B)</c>) and its own unqualified (<c>Pick(A, B)</c>).
    /// </summary>
    public static readonly string Chained = """
        unit UO;
        interface
        type
          TOwner = class
          public
            Title: string;
            procedure Rename(const S: string);
          end;
          TThing = class
          public
            Owner: TOwner;
            function Parent: TOwner;
          end;
          IShape = interface
            function Bounds: TOwner;
          end;
          TUtil = class
          public
            class function Max<T>(const A, B: T): T;
          end;
          THolder<T: TThing; S: IShape> = class
          public
            Item: T;
            Shape: S;
            function Larger(const A, B: T): T;
            function Pick<U>(const A, B: U): U;
            procedure Use;
          end;
        implementation
        procedure TOwner.Rename(const S: string);
        begin
          Title := S;
        end;
        function TThing.Parent: TOwner;
        begin
          Result := Owner;
        end;
        class function TUtil.Max<T>(const A, B: T): T;
        begin
          Result := A;
        end;
        function THolder<T, S>.Larger(const A, B: T): T;
        begin
          Result := TUtil.Max(A, B);
          Result := Pick(A, B);
        end;
        function THolder<T, S>.Pick<U>(const A, B: U): U;
        begin
          Result := B;
        end;
        procedure THolder<T, S>.Use;
        begin
          Item.Owner.Rename(Item.Parent.Title);
          Shape.Bounds.Title := Larger(Item, Item).Owner.Title;
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
