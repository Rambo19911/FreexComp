namespace FreexComp.Compiler.Tests.Binding;

// LANG-990 (b): the kinds of helped types (phase8/helpers-completion.md §15.5; card F10-028), C-442.
public sealed partial class HelperBindingTests
{
    /// <summary>
    /// One program per kind of helped type: the helper <c>TH</c> with a reading method, a method that changes <c>Self</c>, a
    /// property, a class var, static class methods (one with a parameter) and a class property; a variable, a function result
    /// and the type name as receivers. <c>$DECLARATIONS$</c> declares the helped type <c>$TYPE$</c>; the <c>$Q$</c> line is left
    /// out for a helped type whose name cannot stand before a <c>.</c> (<c>string</c>, a keyword).
    /// </summary>
    private const string TargetKindProgram = """
        program P;
        type
        $DECLARATIONS$
          TH = $KEYWORD$ helper for $TYPE$
            class var Hits: Integer;
            function Peek: Integer;
            procedure Touch;
            class function Kind: Integer; static;
            class function Scale(N: Integer): Integer; static;
            property Seen: Integer read Peek;
            class property Total: Integer read Hits;
          end;
        function TH.Peek: Integer;
        begin
          Inc(Hits);
          Result := SizeOf(Self);
        end;
        procedure TH.Touch;
        var
          Old: $TYPE$;
        begin
          Old := Self;
          $TOUCH$;
        end;
        class function TH.Kind: Integer;
        begin
          Result := Hits;
        end;
        class function TH.Scale(N: Integer): Integer;
        begin
          Result := Hits * N;
        end;
        var
          V: $TYPE$;
        function Make: $TYPE$;
        begin
          Result := V;
        end;
        begin
          V.Touch{=TH};
          if V.Peek{=TH} + V.Seen{=TH} + V.Kind{=TH} + V.Hits{=TH} + V.Total{=TH} + Make.Peek{=TH} + Make.Seen{=TH} > 0 then
            V.Hits{=TH} := 0;
          $Q$.Hits{=TH} := $Q$.Kind{=TH} + $Q$.Total{=TH} + $Q$.Scale{=TH}(2) + V.Scale{=TH}(3);
        end.
        """;

    /// <summary>The predeclared ordinal, real and string types (LANG-990 (b), last entry of the table) and the predeclared pointers.</summary>
    private static readonly string[] PredeclaredTargets =
    [
        "ShortInt", "SmallInt", "Integer", "Int64", "NativeInt", "Byte", "Word", "Cardinal", "UInt64", "NativeUInt",
        "Boolean", "ByteBool", "WordBool", "LongBool", "Char", "AnsiChar", "Single", "Double", "Currency",
        "string", "AnsiString", "UTF8String", "RawByteString", "ShortString", "Pointer", "PChar", "PAnsiChar",
    ];

    public static TheoryData<string, string, string, string> TargetKindRows()
    {
        var rows = new TheoryData<string, string, string, string>
        {
            { "a record", "record", "  TRec = record X, Y: Integer; end;", "TRec" },
            { "a closed instance of a generic record", "record", "  TPair<TKey, TValue> = record Key: TKey; Value: TValue; end;", "TPair<Integer, string>" },
            { "an enumeration", "record", "  TColor = (Red, Green, Blue);", "TColor" },
            { "a named subrange", "record", "  TDigit = 0..9;", "TDigit" },
            { "a set", "record", "  TColor = (Red, Green, Blue);\n  TColors = set of TColor;", "TColors" },
            { "a static array", "record", "  TTriple = array[0..2] of Integer;", "TTriple" },
            { "a named dynamic array", "record", "  TInts = array of Integer;", "TInts" },
            { "a typed pointer", "record", "  TRec = record X, Y: Integer; end;\n  PRec = ^TRec;", "PRec" },
            { "a procedural type, procedure", "record", "  TProc = procedure(N: Integer);", "TProc" },
            { "a procedural type, function", "record", "  TIntFunc = function(X: Integer): Integer;", "TIntFunc" },
            { "a procedural type, function without parameters", "record", "  TGet = function: Integer;", "TGet" },
            { "a method-pointer type", "record", "  TNotify = procedure(Sender: TObject) of object;", "TNotify" },
            { "a method-pointer type, function without parameters", "record", "  TGetter = function: Integer of object;", "TGetter" },
            { "a method-reference type", "record", "  TAction = reference to procedure(N: Integer);", "TAction" },
            { "a method-reference type, function without parameters", "record", "  TSource = reference to function: Integer;", "TSource" },
            { "a class-reference type", "record", "  TObjClass = class of TObject;", "TObjClass" },
            { "a short-string type", "record", "  TName = string[10];", "TName" },
            { "a distinct type", "record", "  TMeters = type Integer;", "TMeters" },
            { "a distinct string type", "record", "  TId = type string;", "TId" },
            { "class helper: a closed instance of a generic class", "class", "  TBox<T> = class Item: T; end;", "TBox<Integer>" },
        };
        foreach (var type in PredeclaredTargets)
        {
            rows.Add("predeclared " + type, "record", "", type);
        }

        return rows;
    }

    /// <summary>
    /// LANG-990 (b): a record helper is declared for every named type that is no class — a record, an enumeration, a subrange, a
    /// set, an array, a pointer, a procedural, method-pointer or method-reference type, a class-reference type, a short-string
    /// type, a distinct type, a predeclared type and a closed instance of a generic record — and a class helper for a closed
    /// instance of a generic class; its members bind on a variable, on a function result and (the static ones) on the type name.
    /// A value of a function type without parameters is the receiver itself when the helper of its type declares the name (no
    /// implicit call, LANG-482); a value of a class-reference type takes the instance methods of the helper of that type.
    /// </summary>
    [Theory]
    [MemberData(nameof(TargetKindRows))]
    [Trait("Req", "LANG-990")]
    public void RecordHelper_TargetKinds(string what, string keyword, string declarations, string type)
    {
        var lines = TargetKindProgram.ReplaceLineEndings("\n")
            .Replace("$TOUCH$", keyword == "class" ? "if Old <> nil then Inc(Hits)" : "Self := Old", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => (type != "string" || !line.Contains("$Q$", StringComparison.Ordinal)) && (declarations.Length > 0 || line != "$DECLARATIONS$"));
        var source = string.Join('\n', lines)
            .Replace("$DECLARATIONS$", declarations, StringComparison.Ordinal)
            .Replace("$KEYWORD$", keyword, StringComparison.Ordinal)
            .Replace("$TYPE$", type, StringComparison.Ordinal)
            .Replace("$Q$", type, StringComparison.Ordinal);

        _ = what;
        AssertMarkers([("P.pas", source)]);
    }

    /// <summary>
    /// The edges of three kinds (C-442): a name the helper of a function type does not declare is looked up in the result of the
    /// call, as without a helper (LANG-482); an element of an enumeration comes before a helper member, and a name that neither
    /// has stays E2005, also with arguments; a class-reference value keeps the constructors and class methods of its class.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-990")]
    public void RecordHelper_TargetKinds_NamesTheHelperDoesNotDeclare() => AssertMarkers(
    [
        ("P.pas", """
            program P;
            type
              TGet = function: Integer;
              TColor = (Red, Green, Blue);
              TLeaf = class
                class function Tag: Integer;
              end;
              TLeafClass = class of TLeaf;
              TGetH = record helper for TGet
                function Twice: Integer;
              end;
              TIntH = record helper for Integer
                function Plus(N: Integer): Integer;
              end;
              TColorH = record helper for TColor
                class function Parse(N: Integer): TColor; static;
              end;
              TLeafClassH = record helper for TLeafClass
                function Make: TLeaf;
              end;
            class function TLeaf.Tag: Integer; begin Result := 1 end;
            function TGetH.Twice: Integer; begin Result := Self() + Self() end;
            function TIntH.Plus(N: Integer): Integer; begin Result := Self + N end;
            class function TColorH.Parse(N: Integer): TColor; begin Result := TColor(N) end;
            function TLeafClassH.Make: TLeaf; begin Result := Self.Create end;
            var
              G: TGet;
              C: TColor;
              K: TLeafClass;
              L: TLeaf;
            begin
              if G.Twice{=TGetH} + G.Plus{=TIntH}(1) + K.Tag{=TLeaf} > 0 then
                C := TColor.Parse{=TColorH}(1);
              if (C = TColor.Blue) or (C = TColor.Missing{!E2005}) or (C = TColor.Missing{!E2005}(1)) then
                L := K.Make{=TLeafClassH}
              else
                L := K.Create{=TObject};
              L.Free;
              L := K.Missing{!E2005};
            end.
            """),
    ]);
}
