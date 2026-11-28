using FreexComp.Compiler.Tests.Emit;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>
/// C-422 (CMP-1479): generic constraints through the <c>.pcu</c> — a generic type or generic method imported from a <c>.pcu</c>
/// keeps its constraints, and the classes imported from a <c>.pcu</c> keep what the <c>constructor</c> constraint looks at (the
/// visibility, the <c>overload</c> flag and the default values of their constructors, fields and class functions named
/// <c>Create</c>): the same IR and the same E2704 diagnostics as against the sources. A constraint is bound where the source
/// binds it: it may name the declaring class itself, a class completed later in the type section, a nested type of the declaring
/// or of an enclosing class, a nested type the class inherits, a type of a used unit (also qualified) and a generic instance.
/// </summary>
public sealed partial class PcuTests
{
    /// <summary>The types the instantiations name: every family <c>GenericBindingTests.Constraints_ConstructorFollowsCallLookup</c> has.</summary>
    internal const string ConstraintArgumentsUnit = """
        unit V;
        interface
        type
          TBase = class(TInterfacedObject)
          public
            function Name: string; virtual;
          end;
          TChild = class(TBase)
          public
            function Name: string; override;
          end;
          TOther = class
          end;
          IGreeter = interface
            function Hi: string;
          end;
          ILoud = interface(IGreeter)
            function Loud: string;
          end;
          IOther = interface
            procedure Ho;
          end;
          TGreeter = class(TInterfacedObject, IGreeter)
          public
            function Hi: string;
          end;
          TGreeterKid = class(TGreeter)
          end;
          TLoud = class(TInterfacedObject, ILoud)
          public
            function Hi: string;
            function Loud: string;
          end;
          TRec = record
            X: Integer;
          end;
          PRec = ^TRec;
          TDyn = array of Integer;
          TOwn = class(TBase)
          public
            constructor Create;
            function Name: string; override;
          end;
          TOverload = class(TBase)
          public
            constructor Create(A: Integer); overload;
          end;
          TDefaulted = class(TBase)
          public
            Seed: Integer;
            constructor Create(A: Integer = 7);
            function Name: string; override;
          end;
          TBoth = class(TBase)
          public
            Seed: Integer;
            constructor Create(A: Integer = 9); overload;
            function Name: string; override;
          end;
          TVirt = class(TBase)
          public
            constructor Create; virtual;
          end;
          TVirtKid = class(TVirt)
          public
            constructor Create; override;
            function Name: string; override;
          end;
          TPublished = class(TBase)
          published
            constructor Create;
          end;
          TReopened = class(TBase)
          public
            constructor Create(A: Integer);
          end;
          TReopenedKid = class(TReopened)
          public
            constructor Create;
          end;
          TNoDefault = class
          public
            constructor Create(A: Integer);
          end;
          TNoDefaultKid = class(TNoDefault)
          end;
          THidden = class
          private
            constructor Create;
          end;
          TStrictHidden = class
          strict private
            constructor Create;
          end;
          TProt = class
          protected
            constructor Create;
          end;
          TStrictProt = class
          strict protected
            constructor Create;
          end;
          TProtKid = class(TProt)
          end;
          TProtOver = class
          protected
            constructor Create; overload;
          end;
          TTwo = class
          public
            constructor Create(A: Integer = 0); overload;
            constructor Create(const S: string = ''); overload;
          end;
          TField = class
          public
            Create: Integer;
          end;
          TFunc = class
          public
            class function Create: TFunc;
          end;
        implementation
        function TBase.Name: string; begin Result := 'base'; end;
        function TChild.Name: string; begin Result := 'child'; end;
        function TGreeter.Hi: string; begin Result := 'hi'; end;
        function TLoud.Hi: string; begin Result := 'HI'; end;
        function TLoud.Loud: string; begin Result := 'LOUD'; end;
        constructor TOwn.Create; begin end;
        function TOwn.Name: string; begin Result := 'own'; end;
        constructor TOverload.Create(A: Integer); begin end;
        constructor TDefaulted.Create(A: Integer); begin Seed := A; end;
        function TDefaulted.Name: string; begin Result := 'defaulted'; end;
        constructor TBoth.Create(A: Integer); begin Seed := A; end;
        function TBoth.Name: string; begin Result := 'both'; end;
        constructor TVirt.Create; begin end;
        constructor TVirtKid.Create; begin end;
        function TVirtKid.Name: string; begin Result := 'virtkid'; end;
        constructor TPublished.Create; begin end;
        constructor TReopened.Create(A: Integer); begin end;
        constructor TReopenedKid.Create; begin end;
        constructor TNoDefault.Create(A: Integer); begin end;
        constructor THidden.Create; begin end;
        constructor TStrictHidden.Create; begin end;
        constructor TProt.Create; begin end;
        constructor TStrictProt.Create; begin end;
        constructor TProtOver.Create; begin end;
        constructor TTwo.Create(A: Integer); begin end;
        constructor TTwo.Create(const S: string); begin end;
        class function TFunc.Create: TFunc; begin Result := nil; end;
        end.
        """;

    /// <summary>
    /// The constrained generics: every kind, a shared and a grouped list, generic methods of classes and of a record, an
    /// instantiation in a generic body; constraint types that are a forward-declared class (<c>TTree</c>), the declaring class
    /// (<c>TNode.Same</c>), a qualified type of a used unit (<c>TQualified</c>), a generic interface instance (<c>TUnbox</c>), a
    /// nested type of the declaring class (<c>THost.Tagging</c>), of the enclosing class (<c>THost.TCell</c>, <c>THost.TNested.Tagged</c>)
    /// and of an ancestor (<c>THostKid</c>, <c>THostPlainKid</c>); a generic method whose heading names a nested type (<c>Wrap</c>).
    /// </summary>
    internal const string ConstraintGenericsUnit = """
        unit U;
        interface
        uses V;
        type
          TFactory<T: class, constructor> = class
          public
            class function Make: T;
          end;
          TMaker<T: constructor> = class
          public
            class function Make: T;
          end;
          TNamer<T: TBase> = class
          public
            class function NameOf(const Item: T): string;
          end;
          TMap<K: record; V: class> = class
          public
            Key: K;
            Value: V;
            function Filled: Boolean;
          end;
          TPairOf<A, B: class> = class
          public
            First: A;
            Second: B;
          end;
          TLouder<T: TBase, IGreeter> = class
          public
            class function Both(const Item: T): string;
          end;
          TUtil = class
          public
            class function Greet<T: IGreeter>(const Item: T): string; static;
            class function Fresh<T: TBase, constructor>: string; static;
            function Keep<T: class>(const Item: T): Boolean;
          end;
          TOuter<T> = class
          public
            class function Inner: string;
          end;
          TNode = class;
          TTree<T: TNode> = class
          public
            class function DepthOf(const Item: T): Integer;
          end;
          TNode = class
          public
            function Depth: Integer; virtual;
            function Same<T: TNode>(const Item: T): Boolean;
          end;
          TLeaf = class(TNode)
          public
            function Depth: Integer; override;
          end;
          TQualified<T: V.TBase; G: V.IGreeter> = class
          public
            class function Named(const Item: T): string;
          end;
          IBoxed<T> = interface
            function Get: T;
          end;
          TIntBox = class(TInterfacedObject, IBoxed<Integer>)
          public
            function Get: Integer;
          end;
          TUnbox<T: IBoxed<Integer>> = class
          public
            class function Twice(const Item: T): Integer;
          end;
          THost = class
          public
            type
              TInner = class
              public
                function Tag: string; virtual;
              end;
              TInnerKid = class(TInner)
              public
                function Tag: string; override;
              end;
              TCell<T: TInner> = class
              public
                class function TagOf(const Item: T): string;
              end;
              TNested = class
              public
                class function Tagged<T: TInner>(const Item: T): string; static;
              end;
            class function Tagging<T: TInner>(const Item: T): string; static;
            class function Wrap<T>(const Item: T; const Inner: TInner): string; static;
          end;
          THostKid = class(THost)
          public
            type
              TKidCell<T: TInner> = class
              public
                class function TagOf(const Item: T): string;
              end;
            class function Sub<T: TInner>(const Item: T): string; static;
            class function Wrap<T>(const Item: T; const Inner: TInner): string; static;
          end;
          THostPlainKid = class(THost)
          public
            class function Sub<T: TInner>(const Item: T): string; static;
          end;
          TRecUtil = record
            class function Size<T: record>(const Item: T): Integer; static;
            class function Fresh<T: class, constructor>: Boolean; static;
          end;
        function Plain: Integer;
        implementation
        class function TFactory<T>.Make: T;
        begin
          Result := T.Create;
        end;
        class function TMaker<T>.Make: T;
        begin
          Result := T.Create;
        end;
        class function TNamer<T>.NameOf(const Item: T): string;
        begin
          if Item = nil then
            Result := ''
          else
            Result := Item.Name;
        end;
        function TMap<K, V>.Filled: Boolean;
        begin
          Result := Value <> nil;
        end;
        class function TLouder<T>.Both(const Item: T): string;
        begin
          Result := Item.Name + Item.Hi;
        end;
        class function TUtil.Greet<T>(const Item: T): string;
        begin
          if Item = nil then
            Result := ''
          else
            Result := Item.Hi;
        end;
        class function TUtil.Fresh<T>: string;
        var
          Made: T;
        begin
          Made := T.Create;
          Result := Made.Name;
          Made.Free;
        end;
        function TUtil.Keep<T>(const Item: T): Boolean;
        begin
          Result := Item <> nil;
        end;
        class function TOuter<T>.Inner: string;
        var
          Made: TBase;
        begin
          Made := TFactory<T>.Make;
          Result := Made.Name;
          Made.Free;
        end;
        class function TTree<T>.DepthOf(const Item: T): Integer;
        begin
          Result := Item.Depth;
        end;
        function TNode.Depth: Integer;
        begin
          Result := 1;
        end;
        function TNode.Same<T>(const Item: T): Boolean;
        begin
          Result := Item = Self;
        end;
        function TLeaf.Depth: Integer;
        begin
          Result := 2;
        end;
        class function TQualified<T, G>.Named(const Item: T): string;
        begin
          Result := Item.Name;
        end;
        function TIntBox.Get: Integer;
        begin
          Result := 21;
        end;
        class function TUnbox<T>.Twice(const Item: T): Integer;
        begin
          Result := Item.Get * 2;
        end;
        function THost.TInner.Tag: string;
        begin
          Result := 'inner';
        end;
        function THost.TInnerKid.Tag: string;
        begin
          Result := 'kid';
        end;
        class function THost.TCell<T>.TagOf(const Item: T): string;
        begin
          Result := Item.Tag;
        end;
        class function THost.TNested.Tagged<T>(const Item: T): string;
        begin
          Result := Item.Tag;
        end;
        class function THost.Tagging<T>(const Item: T): string;
        begin
          Result := Item.Tag;
        end;
        class function THost.Wrap<T>(const Item: T; const Inner: TInner): string;
        begin
          Result := Inner.Tag;
        end;
        class function THostKid.TKidCell<T>.TagOf(const Item: T): string;
        begin
          Result := Item.Tag;
        end;
        class function THostKid.Sub<T>(const Item: T): string;
        begin
          Result := Item.Tag;
        end;
        class function THostKid.Wrap<T>(const Item: T; const Inner: TInner): string;
        begin
          Result := Inner.Tag;
        end;
        class function THostPlainKid.Sub<T>(const Item: T): string;
        begin
          Result := Item.Tag;
        end;
        class function TRecUtil.Size<T>(const Item: T): Integer;
        begin
          Result := SizeOf(Item);
        end;
        class function TRecUtil.Fresh<T>: Boolean;
        var
          Made: T;
        begin
          Made := T.Create;
          Result := Made <> nil;
          Made.Free;
        end;
        function Plain: Integer;
        begin
          Result := 1;
        end;
        end.
        """;

    /// <summary>
    /// CMP-1479, LANG-983: a program instantiating the constrained generics of a <c>.pcu</c>-only unit with the types of another
    /// <c>.pcu</c>-only unit gives the IR it gets against their sources — <c>T.Create</c> calls the constructor the call
    /// <c>X.Create</c> resolves to (an own one, the ancestor's next to a <c>Create(…)</c> with <c>overload</c>, one whose
    /// parameters have default values, a virtual one, a published one), and the generic methods bind their headings where the
    /// members of their type were bound (nested types of the type, of its enclosing type and of its ancestors).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1479")]
    [Trait("Req", "LANG-983")]
    public void GenericConstraintsFromPcu()
    {
        const string Program = """
            program P;
            uses U, V;
            type
              TLoudBase = class(TBase, IGreeter)
              public
                function Hi: string;
              end;
            function TLoudBase.Hi: string; begin Result := '!'; end;
            procedure Show(Item: TBase);
            begin
              Writeln(Item.Name);
              Item.Free;
            end;
            var
              M: TMap<Integer, TChild>;
              R: TMap<TRec, TOther>;
              Q: TPairOf<TBase, TGreeter>;
              G: IGreeter;
              L: ILoud;
              B: TLoudBase;
              W: TUtil;
              N: TNode;
              F: TLeaf;
              X: TIntBox;
              I: THost.TInnerKid;
            begin
              Show(TFactory<TBase>.Make);
              Show(TFactory<TChild>.Make);
              Show(TFactory<TOwn>.Make);
              Show(TFactory<TOverload>.Make);
              Show(TFactory<TDefaulted>.Make);
              Show(TFactory<TBoth>.Make);
              Show(TFactory<TVirt>.Make);
              Show(TFactory<TVirtKid>.Make);
              Show(TFactory<TPublished>.Make);
              Show(TFactory<TReopenedKid>.Make);
              Show(TMaker<TDefaulted>.Make);
              Writeln(TNamer<TBase>.NameOf(nil) = '', TNamer<TChild>.NameOf(nil) = '');
              M := TMap<Integer, TChild>.Create;
              R := TMap<TRec, TOther>.Create;
              Q := TPairOf<TBase, TGreeter>.Create;
              Writeln(M.Filled, R.Filled, Q.First = nil);
              B := TLoudBase.Create;
              Writeln(TLouder<TLoudBase>.Both(B));
              G := TGreeter.Create;
              L := TLoud.Create;
              Writeln(TUtil.Greet<IGreeter>(G), TUtil.Greet(L), TUtil.Greet<TGreeterKid>(nil) = '');
              Writeln(TUtil.Fresh<TOwn>, TUtil.Fresh<TDefaulted>);
              W := TUtil.Create;
              Writeln(W.Keep<TOther>(nil), W.Keep(B));
              Writeln(TOuter<TBoth>.Inner);
              N := TNode.Create;
              F := TLeaf.Create;
              Writeln(TTree<TNode>.DepthOf(N), TTree<TLeaf>.DepthOf(F), N.Same<TLeaf>(F), F.Same(F));
              Writeln(TQualified<TChild, ILoud>.Named(nil) = '');
              X := TIntBox.Create;
              Writeln(TUnbox<TIntBox>.Twice(X), TUnbox<IBoxed<Integer>>.Twice(X));
              I := THost.TInnerKid.Create;
              Writeln(THost.TCell<THost.TInnerKid>.TagOf(I), THost.TNested.Tagged<THost.TInner>(I), THost.TNested.Tagged(I), THost.Tagging(I));
              Writeln(THostKid.TKidCell<THost.TInnerKid>.TagOf(I), THostKid.Sub(I), THostPlainKid.Sub<THost.TInner>(I));
              Writeln(THost.Wrap<Integer>(1, I), THostKid.Wrap('a', I));
              Writeln(TRecUtil.Size<Int64>(5), TRecUtil.Size(M.Key), TRecUtil.Fresh<TOwn>, TRecUtil.Fresh<TDefaulted>);
            end.
            """;

        PcuImportTests.AssertSameIrAgainstPcu(ConstraintGenericsUnit, Program, ConstraintArgumentsUnit);
    }

    /// <summary>
    /// CMP-1479, LANG-984: a program violating each constraint kind of the generics of a <c>.pcu</c>-only unit — with types of
    /// another <c>.pcu</c>-only unit — gets the E2704 list (text and position) it gets against the sources: <c>class</c>,
    /// <c>record</c>, a class type, an interface type, shared and grouped lists, generic methods with written and inferred
    /// arguments, an instantiation in a generic body (E2710), every way a class misses <c>constructor</c> (a hidden
    /// parameterless <c>Create</c>, a private or protected one, an ambiguous call, a field, a class function), and constraint types
    /// the <c>.pcu</c> reader lost before C-422 (the declaring class, nested types of the declaring, the enclosing or an ancestor
    /// class): the violations were accepted, or reported from the instance's body (E2710) instead of at the instantiation.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1479")]
    [Trait("Req", "LANG-984")]
    public void GenericConstraintViolationFromPcu_SameDiagnostics()
    {
        const string Program = """
            program P;
            uses U, V;
            var
              F01: TFactory<Integer>;
              F02: TFactory<TNoDefault>;
              F03: TFactory<TNoDefaultKid>;
              F04: TFactory<THidden>;
              F05: TFactory<TStrictHidden>;
              F06: TFactory<TProt>;
              F07: TFactory<TStrictProt>;
              F08: TFactory<TProtKid>;
              F09: TFactory<TProtOver>;
              F10: TFactory<TTwo>;
              F11: TFactory<TField>;
              F12: TFactory<TFunc>;
              F13: TFactory<TReopened>;
              F14: TFactory<IGreeter>;
              K01: TMaker<TRec>;
              K02: TMaker<THidden>;
              N01: TNamer<TOther>;
              N02: TNamer<IGreeter>;
              M01: TMap<string, Integer>;
              M02: TMap<TBase, TRec>;
              M03: TMap<TDyn, PRec>;
              Q01: TPairOf<TBase, Integer>;
              Q02: TPairOf<TRec, IOther>;
              L01: TLouder<TBase>;
              L02: TLouder<TGreeter>;
              W: TUtil;
              O: TOther;
              T01: TTree<TOther>;
              T02: TQualified<TOther, IOther>;
              T03: TUnbox<TGreeter>;
              T04: THost.TCell<TBase>;
              T05: THostKid.TKidCell<TOther>;
              N: TNode;
            begin
              O := nil;
              Writeln(TUtil.Greet<TBase>(nil));
              Writeln(TUtil.Greet<IOther>(nil));
              Writeln(TUtil.Greet(O));
              Writeln(TUtil.Fresh<TOther>);
              Writeln(TUtil.Fresh<TReopened>);
              W := nil;
              Writeln(W.Keep<Integer>(5));
              Writeln(W.Keep(5));
              Writeln(TOuter<THidden>.Inner);
              Writeln(TOuter<Integer>.Inner);
              N := nil;
              Writeln(N.Same<TOther>(O));
              Writeln(N.Same(O));
              Writeln(THost.TNested.Tagged<TOther>(O));
              Writeln(THost.TNested.Tagged(O));
              Writeln(THost.Tagging<TBase>(nil));
              Writeln(THost.Tagging(O));
              Writeln(TRecUtil.Size<string>('a'));
              Writeln(TRecUtil.Size(O));
              Writeln(TRecUtil.Fresh<THidden>);
              Writeln(TRecUtil.Fresh<TNoDefault>);
              Writeln(THostKid.Sub(O));
              Writeln(THostPlainKid.Sub<TBase>(nil));
            end.
            """;
        string[] expected =
        [
            "P.pas(4,17): E2704 Type argument 'Integer' does not satisfy constraint 'class' of 'T'",
            "P.pas(4,17): E2704 Type argument 'Integer' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(5,17): E2704 Type argument 'TNoDefault' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(6,17): E2704 Type argument 'TNoDefaultKid' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(7,17): E2704 Type argument 'THidden' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(8,17): E2704 Type argument 'TStrictHidden' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(9,17): E2704 Type argument 'TProt' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(10,17): E2704 Type argument 'TStrictProt' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(11,17): E2704 Type argument 'TProtKid' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(12,17): E2704 Type argument 'TProtOver' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(13,17): E2704 Type argument 'TTwo' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(14,17): E2704 Type argument 'TField' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(15,17): E2704 Type argument 'TFunc' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(16,17): E2704 Type argument 'TReopened' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(17,17): E2704 Type argument 'IGreeter' does not satisfy constraint 'class' of 'T'",
            "P.pas(17,17): E2704 Type argument 'IGreeter' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(18,15): E2704 Type argument 'TRec' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(19,15): E2704 Type argument 'THidden' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(20,15): E2704 Type argument 'TOther' does not satisfy constraint 'TBase' of 'T'",
            "P.pas(21,15): E2704 Type argument 'IGreeter' does not satisfy constraint 'TBase' of 'T'",
            "P.pas(22,13): E2704 Type argument 'string' does not satisfy constraint 'record' of 'K'",
            "P.pas(22,21): E2704 Type argument 'Integer' does not satisfy constraint 'class' of 'V'",
            "P.pas(23,13): E2704 Type argument 'TBase' does not satisfy constraint 'record' of 'K'",
            "P.pas(23,20): E2704 Type argument 'TRec' does not satisfy constraint 'class' of 'V'",
            "P.pas(24,13): E2704 Type argument 'TDyn' does not satisfy constraint 'record' of 'K'",
            "P.pas(24,19): E2704 Type argument 'PRec' does not satisfy constraint 'class' of 'V'",
            "P.pas(25,23): E2704 Type argument 'Integer' does not satisfy constraint 'class' of 'B'",
            "P.pas(26,16): E2704 Type argument 'TRec' does not satisfy constraint 'class' of 'A'",
            "P.pas(26,22): E2704 Type argument 'IOther' does not satisfy constraint 'class' of 'B'",
            "P.pas(27,16): E2704 Type argument 'TBase' does not satisfy constraint 'IGreeter' of 'T'",
            "P.pas(28,16): E2704 Type argument 'TGreeter' does not satisfy constraint 'TBase' of 'T'",
            "P.pas(31,14): E2704 Type argument 'TOther' does not satisfy constraint 'TNode' of 'T'",
            "P.pas(32,19): E2704 Type argument 'TOther' does not satisfy constraint 'TBase' of 'T'",
            "P.pas(32,27): E2704 Type argument 'IOther' does not satisfy constraint 'IGreeter' of 'G'",
            "P.pas(33,15): E2704 Type argument 'TGreeter' does not satisfy constraint 'IBoxed<Integer>' of 'T'",
            "P.pas(34,20): E2704 Type argument 'TBase' does not satisfy constraint 'TInner' of 'T'",
            "P.pas(35,26): E2704 Type argument 'TOther' does not satisfy constraint 'TInner' of 'T'",
            "P.pas(39,23): E2704 Type argument 'TBase' does not satisfy constraint 'IGreeter' of 'T'",
            "P.pas(40,23): E2704 Type argument 'IOther' does not satisfy constraint 'IGreeter' of 'T'",
            "P.pas(41,17): E2704 Type argument 'TOther' does not satisfy constraint 'IGreeter' of 'T'",
            "P.pas(42,23): E2704 Type argument 'TOther' does not satisfy constraint 'TBase' of 'T'",
            "P.pas(43,23): E2704 Type argument 'TReopened' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(45,18): E2704 Type argument 'Integer' does not satisfy constraint 'class' of 'T'",
            "P.pas(46,13): E2704 Type argument 'Integer' does not satisfy constraint 'class' of 'T'",
            "P.pas(50,18): E2704 Type argument 'TOther' does not satisfy constraint 'TNode' of 'T'",
            "P.pas(51,13): E2704 Type argument 'TOther' does not satisfy constraint 'TNode' of 'T'",
            "P.pas(52,32): E2704 Type argument 'TOther' does not satisfy constraint 'TInner' of 'T'",
            "P.pas(53,25): E2704 Type argument 'TOther' does not satisfy constraint 'TInner' of 'T'",
            "P.pas(54,25): E2704 Type argument 'TBase' does not satisfy constraint 'TInner' of 'T'",
            "P.pas(55,17): E2704 Type argument 'TOther' does not satisfy constraint 'TInner' of 'T'",
            "P.pas(56,25): E2704 Type argument 'string' does not satisfy constraint 'record' of 'T'",
            "P.pas(57,20): E2704 Type argument 'TOther' does not satisfy constraint 'record' of 'T'",
            "P.pas(58,26): E2704 Type argument 'THidden' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(59,26): E2704 Type argument 'TNoDefault' does not satisfy constraint 'constructor' of 'T'",
            "P.pas(60,20): E2704 Type argument 'TOther' does not satisfy constraint 'TInner' of 'T'",
            "P.pas(61,29): E2704 Type argument 'TBase' does not satisfy constraint 'TInner' of 'T'",
            "U.pas(159,20): E2710 In instantiation 'TOuter<THidden>': Type argument 'THidden' does not satisfy constraint 'constructor' of 'T'",
            "U.pas(159,20): E2710 In instantiation 'TOuter<Integer>': Type argument 'Integer' does not satisfy constraint 'class' of 'T'",
            "U.pas(159,20): E2710 In instantiation 'TOuter<Integer>': Type argument 'Integer' does not satisfy constraint 'constructor' of 'T'",
        ];

        var againstSources = ConstraintDiagnostics(Files(u: ConstraintGenericsUnit, v: ConstraintArgumentsUnit, program: Program), Options());
        var pcus = Pcus(Files(u: ConstraintGenericsUnit, v: ConstraintArgumentsUnit, program: "program P;\nuses U, V;\nbegin\nend.\n"));
        var fs = new InMemoryFileSystem().Add(Path.Combine(Dir, "P.pas"), Program).AddSystem();
        foreach (var name in new[] { "U", "V" })
        {
            fs.Add(Path.Combine(PcuImportTests.Lib, name + ".pcu"), pcus[name]).Add(Path.Combine(PcuImportTests.Lib, name + ".obj"), "");
        }

        var againstPcus = ConstraintDiagnostics(fs, Options() with { SearchPaths = [PcuImportTests.Lib] });

        Assert.True(expected.SequenceEqual(againstSources), "against the sources:\n" + string.Join("\n", againstSources));
        Assert.True(expected.SequenceEqual(againstPcus), "against the .pcu files:\n" + string.Join("\n", againstPcus));
    }

    /// <summary>The errors of a check of the program, as <c>file(line,col): CODE message</c> in the order reported.</summary>
    private static string[] ConstraintDiagnostics(InMemoryFileSystem fs, CompilationOptions options)
    {
        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        return [.. result.Diagnostics.Where(d => d.Code[0] == 'E')
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}")];
    }

    /// <summary>
    /// CMP-1479 (CMP-152): a constraint is part of the generic's interface — adding, changing or removing one on a generic type or
    /// on a generic method changes the interface hash of the unit; an edit of a body that is no generic's does not.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1479")]
    [Trait("Req", "CMP-152")]
    [InlineData("added to a generic type", "TOuter<T> = class", "TOuter<T: class> = class", true)]
    [InlineData("removed from a generic type", "TNamer<T: TBase> = class", "TNamer<T> = class", true)]
    [InlineData("another keyword", "TMaker<T: constructor> = class", "TMaker<T: class> = class", true)]
    [InlineData("another constraint type", "TNamer<T: TBase> = class", "TNamer<T: TChild> = class", true)]
    [InlineData("one more item", "TPairOf<A, B: class> = class", "TPairOf<A, B: class, constructor> = class", true)]
    [InlineData("regrouped", "TPairOf<A, B: class> = class", "TPairOf<A; B: class> = class", true)]
    [InlineData("added to a generic method", "function Keep<T: class>(const Item: T): Boolean;", "function Keep<T: class, constructor>(const Item: T): Boolean;", true)]
    [InlineData("removed from a generic method", "function Keep<T: class>(const Item: T): Boolean;", "function Keep<T>(const Item: T): Boolean;", true)]
    [InlineData("a body that is no generic's", "Result := 1;", "Result := 2;", false)]
    public void InterfaceHash_ChangesWithAConstraint(string edit, string from, string to, bool changes)
    {
        const string Program = "program P;\nuses U, V;\nbegin\nend.\n";
        var before = Hash("U", Files(u: ConstraintGenericsUnit, v: ConstraintArgumentsUnit, program: Program));

        var after = Hash("U", Files(u: ReplaceOnce(ConstraintGenericsUnit, from, to), v: ConstraintArgumentsUnit, program: Program));

        Assert.True(changes ? before != after : before == after, edit);
    }
}
