using System.Text.Json.Nodes;
using FreexComp.Compiler.Tests.Emit;

namespace FreexComp.Compiler.Tests.Units;

public sealed partial class PcuTests
{
    internal const string NestedGenericUnit = """
        unit U;
        interface
        type
          TOuter = class
          public
            type
              TCell<T> = class
              public
                Item: T;
                function Show: string;
              end;
              TPair<A, B> = record
                First: A;
                Second: B;
              end;
            function Make(N: Integer): TCell<Integer>;
          end;
          TList<T> = class
          public
            type
              TEnumerator = class
              private
                FList: TList<T>;
                FIndex: Integer;
              public
                constructor Create(AList: TList<T>);
                function MoveNext: Boolean;
                function GetCurrent: T;
                property Current: T read GetCurrent;
              end;
              TNode = record
                Value: T;
              end;
              TBranch<K> = class
              public
                Left: T;
                Right: K;
                function Both: string;
              end;
          private
            FItems: array of T;
          public
            procedure Add(const V: T);
            function GetEnumerator: TEnumerator;
          end;
        implementation
        function TOuter.TCell<T>.Show: string;
        begin
          Result := ClassName;
        end;
        function TOuter.Make(N: Integer): TCell<Integer>;
        begin
          Result := TCell<Integer>.Create;
          Result.Item := N;
        end;
        procedure TList<T>.Add(const V: T);
        begin
          SetLength(FItems, Length(FItems) + 1);
          FItems[High(FItems)] := V;
        end;
        function TList<T>.GetEnumerator: TEnumerator;
        begin
          Result := TEnumerator.Create(Self);
        end;
        constructor TList<T>.TEnumerator.Create(AList: TList<T>);
        begin
          inherited Create;
          FList := AList;
          FIndex := -1;
        end;
        function TList<T>.TEnumerator.MoveNext: Boolean;
        begin
          Inc(FIndex);
          Result := FIndex < Length(FList.FItems);
        end;
        function TList<T>.TEnumerator.GetCurrent: T;
        begin
          Result := FList.FItems[FIndex];
        end;
        function TList<T>.TBranch<K>.Both: string;
        begin
          Result := ClassName;
        end;
        end.
        """;

    /// <summary>
    /// CMP-1392 b (C-345): a generic nested in an interface-part class is a <c>generic</c> entry attached to its enclosing type, with
    /// its declaration and method implementations as body pieces; a program binds <c>TOuter.TCell&lt;X&gt;</c> (also unit-qualified)
    /// against the <c>.pcu</c> to the same IR as against the source.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1392")]
    [Trait("Req", "LANG-778")]
    public void GenericNestedInClassFromPcu()
    {
        const string Program = """
            program P;
            uses U;
            var
              O: TOuter;
              C: TOuter.TCell<Integer>;
              S: U.TOuter.TCell<string>;
              R: TOuter.TPair<Byte, string>;
            begin
              O := TOuter.Create;
              C := O.Make(3);
              Writeln(C.Show, C.Item, C.ClassType = TOuter.TCell<Integer>);
              S := U.TOuter.TCell<string>.Create;
              S.Item := 'x';
              Writeln(S.Show, S.Item);
              R.First := 1;
              R.Second := 'two';
              Writeln(R.First, R.Second, SizeOf(TOuter.TPair<Byte, Int64>));
            end.
            """;

        var pcu = Pcu("U", Files(u: NestedGenericUnit, program: Program));
        var types = (JsonArray)pcu["interface"]!["types"]!;
        var outer = types.Single(t => (string?)t!["name"] == "TOuter")!;
        var nested = ((JsonArray)outer["nested"]!).Select(n => ((string)n!["name"]!, (string)types[(int)n["type"]!]!["kind"]!)).ToList();
        Assert.Equal([("TCell", "generic"), ("TPair", "generic")], nested);
        var cell = types[(int)outer["nested"]![0]!["type"]!]!;
        var body = (string)pcu["genericBodies"]![(int)cell["body"]!]!["text"]!;
        Assert.Contains("function TOuter.TCell<T>.Show: string;", body, StringComparison.Ordinal);
        PcuImportTests.AssertSameIrAgainstPcu(NestedGenericUnit, Program);
    }

    /// <summary>
    /// CMP-1392 b (C-345): the body pieces of a generic hold all implementations whose path starts with it — also those of its nested
    /// class and of a generic nested in it — so the nested types of instances made against the <c>.pcu</c> give the same IR.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1392")]
    [Trait("Req", "LANG-775")]
    public void NestedTypeOfGenericFromPcu_GivesTheSameIrAsAgainstTheSource()
    {
        const string Program = """
            program P;
            uses U;
            var
              L: TList<Integer>;
              E: TList<Integer>.TEnumerator;
              N: U.TList<string>.TNode;
              B: TList<Integer>.TBranch<string>;
              I: Integer;
            begin
              L := TList<Integer>.Create;
              L.Add(1);
              L.Add(2);
              for I in L do
                Writeln(I);
              E := L.GetEnumerator;
              Writeln(E.MoveNext, E.Current, E.ClassName);
              N.Value := 'n';
              B := TList<Integer>.TBranch<string>.Create;
              Writeln(B.Both, N.Value, TClass(TList<Integer>.TEnumerator) <> TClass(TList<string>.TEnumerator));
            end.
            """;

        var pcu = Pcu("U", Files(u: NestedGenericUnit, program: Program));
        var list = ((JsonArray)pcu["interface"]!["types"]!).Single(t => (string?)t!["name"] == "TList")!;
        var body = (string)pcu["genericBodies"]![(int)list["body"]!]!["text"]!;
        Assert.Contains("function TList<T>.TEnumerator.MoveNext: Boolean;", body, StringComparison.Ordinal);
        Assert.Contains("function TList<T>.TBranch<K>.Both: string;", body, StringComparison.Ordinal);
        PcuImportTests.AssertSameIrAgainstPcu(NestedGenericUnit, Program);
    }
}
