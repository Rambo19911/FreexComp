// The diagnostics of an in-memory compilation are read synchronously; the optional token is left out on purpose.
#pragma warning disable xUnit1051

using FreexComp.Compiler.Binding;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

// LANG-992, LANG-994: one member lookup everywhere (phase8/helpers-completion.md §15.3; card F10-027), C-440 — implicit Self and
// with, one overload set across the helper places and the helped type, class-reference values and helper-qualified static
// members, E2005 for a missing member of a type with an active helper, helper methods as values, the hint H2012.
public sealed partial class HelperBindingTests
{
    /// <summary>
    /// "Owner.Name" of the routine a statement calls (through <c>with</c>, a one-statement block, <c>Writeln</c> with one argument,
    /// an assignment and implicit conversions), "parameter X" for a parameter read; the dump otherwise.
    /// </summary>
    private static string Called(BoundNode node) => node switch
    {
        BoundWithStatement with => Called(with.Body),
        BoundBlock { Statements: [var only] } => Called(only),
        BoundWriteStatement { Arguments: [var argument] } => Called(argument.Value),
        BoundAssignmentStatement assignment => Called(assignment.Value),
        BoundConversionExpression conversion => Called(conversion.Operand),
        BoundCallExpression call => $"{call.Routine.ContainingSymbol!.Name}.{call.Routine.Name}",
        BoundCallStatement call => $"{call.Routine.ContainingSymbol!.Name}.{call.Routine.Name}",
        BoundParameterExpression parameter => "parameter " + parameter.Parameter.Name,
        _ => BoundDump.Of(node),
    };

    // ------------------------------------------------------------------ LANG-992 (d): implicit Self and with

    /// <summary>
    /// LANG-992 (d): an unqualified name in a method of a class, of a descendant class and of a record, and a name inside
    /// <c>with</c> on a class or a record value, is looked up by LANG-992 steps 1…3 — the active helper's members come before the
    /// own members of the level it helps; a class method sees the static ones (E2608 for an instance member); parameters and
    /// locals still win; a helper declared after the body or in a unit the code does not use is not seen; a helper method named in
    /// a read-only <c>with</c> is a call on a read-only value (E2752).
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-992")]
    public void Lookup_ImplicitSelfAndWith()
    {
        var (result, _) = Bind("""
            program P;
            type
              TBox = class
                function Size: Integer;
                function Name: string;
                procedure Run;
                procedure Shadow(Twice: Integer);
                class procedure ClassRun;
              end;
              TBig = class(TBox)
                procedure BigRun;
              end;
              TPair = record
                A, B: Integer;
                function Total: Integer;
              end;
              TBoxHelper = class helper for TBox
                function Twice: Integer;
                function Name: string;
                class function Kind: string; static;
              end;
              TPairHelper = record helper for TPair
                function Sum: Integer;
              end;
            function TBox.Size: Integer; begin Result := 1; end;
            function TBox.Name: string; begin Result := 'box'; end;
            function TBoxHelper.Twice: Integer; begin Result := Size * 2; end;
            function TBoxHelper.Name: string; begin Result := 'helped'; end;
            class function TBoxHelper.Kind: string; begin Result := 'kind'; end;
            function TPairHelper.Sum: Integer; begin Result := A + B; end;
            procedure TBox.Run;
            begin
              Writeln(Twice);
              Writeln(Name);
              Writeln(Kind);
            end;
            procedure TBox.Shadow(Twice: Integer);
            begin
              Writeln(Twice);
            end;
            class procedure TBox.ClassRun;
            begin
              Writeln(Kind);
              Writeln(Twice);
            end;
            procedure TBig.BigRun;
            begin
              Writeln(Twice);
            end;
            function TPair.Total: Integer;
            begin
              Result := Sum;
            end;
            var
              B: TBox;
              Pr: TPair;
            begin
              with B do Writeln(Twice);
              with Pr do Writeln(Sum);
            end.
            """);

        Assert.Equal(["H2012(19,14): Helper member 'Name' hides 'TBox.Name'",
            "E2608(44,11): Instance member 'Twice' is not accessible from a class method"], Format(result.Diagnostics));
        Assert.Equal(["TBoxHelper.Twice", "TBoxHelper.Name", "TBoxHelper.Kind"], BodyOf(result, "TBox", "Run").Select(Called));
        Assert.Equal(["parameter Twice"], BodyOf(result, "TBox", "Shadow").Select(Called));                // parameters win
        Assert.Equal("TBoxHelper.Kind", Called(BodyOf(result, "TBox", "ClassRun")[0]));
        Assert.Equal(["TBoxHelper.Twice"], BodyOf(result, "TBig", "BigRun").Select(Called));               // an ancestor level
        Assert.Equal(["TPairHelper.Sum"], BodyOf(result, "TPair", "Total").Select(Called));                // a record method
        Assert.Equal(["TBoxHelper.Twice", "TPairHelper.Sum"], result.Bodies[0].Statements.Select(Called));  // with

        Assert.Equal(["E2003(8,11): Undeclared identifier 'Twice'"], BindDiagnostics("""
            program P;
            type
              TBox = class
                procedure Run;
              end;
            procedure TBox.Run;
            begin
              Writeln(Twice);
            end;
            type
              TLate = class helper for TBox
                function Twice: Integer;
              end;
            function TLate.Twice: Integer; begin Result := 2; end;
            begin
            end.
            """));

        AssertMarkers([
            ("UBox.pas", """
                unit UBox;
                interface
                type
                  TBox = class
                    function Run: Integer;
                  end;
                implementation
                function TBox.Run: Integer;
                begin
                  Result := Twice{!E2003};
                end;
                end.
                """),
            ("UH.pas", """
                unit UH;
                interface
                uses UBox;
                type
                  TBoxH = class helper for TBox
                    function Twice: Integer;
                  end;
                implementation
                function TBoxH.Twice: Integer; begin Result := 2; end;
                end.
                """),
            ("P.pas", """
                program P;
                uses UBox, UH;
                var B: TBox;
                begin
                  B := nil;
                  Writeln(B.Twice{=TBoxH});
                end.
                """),
        ]);

        AssertMarkers([
            ("P.pas", """
                program P;
                type
                  TPair = record A, B: Integer; end;
                  TPairH = record helper for TPair
                    procedure Bump;
                  end;
                procedure TPairH.Bump; begin A := A + 1; end;
                procedure Use(const Q: TPair);
                begin
                  with Q do Bump{!E2752};
                end;
                begin
                end.
                """),
        ]);
    }

    /// <summary>
    /// LANG-991 (d) with LANG-992 (C-440, lead note of C-439): <c>inherited Name</c> in a method of a helper for <c>T</c> goes on after
    /// the helper's chain and <c>T</c>'s own members with the ancestor levels of <c>T</c>, each with the chain of its active helper first.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-991")]
    public void Inherited_ReachesAncestorLevelHelpers()
    {
        var (result, _) = Bind("""
            program P;
            {$WARN HELPER_HIDES_MEMBER OFF}
            type
              TBase = class
                function Name: string;
                function Plain: string;
              end;
              TMid = class(TBase)
              end;
              TBaseH = class helper for TBase
                function Name: string;
              end;
              TMidH = class helper for TMid
                function Name: string;
                function Plain: string;
              end;
            function TBase.Name: string; begin Result := 'base'; end;
            function TBase.Plain: string; begin Result := 'plain'; end;
            function TBaseH.Name: string; begin Result := 'base helper'; end;
            function TMidH.Name: string;
            begin
              Result := inherited Name;
            end;
            function TMidH.Plain: string;
            begin
              Result := inherited Plain;
            end;
            begin
            end.
            """);

        Assert.Equal([], Format(result.Diagnostics));
        Assert.Equal(["TBaseH.Name static"], BodyOf(result, "TMidH", "Name").Select(CallOf));      // the helper of the ancestor level
        Assert.Equal(["TBase.Plain static"], BodyOf(result, "TMidH", "Plain").Select(CallOf));     // then that level's own member
    }

    // ------------------------------------------------------------------ LANG-992 step 3: one overload set

    private const string OverloadProgram = """
        program P;
        {$WARN HELPER_HIDES_MEMBER OFF}
        type
          TBase = class
            procedure Show(N: Integer); overload;
            procedure Grow(N: Integer); overload;
          end;
          TBox = class(TBase)
            procedure Show; overload;
            procedure Hide; overload;
            procedure Same(N: Integer); overload;
            procedure Grow(const S: string; N: Integer); overload;
            procedure Amb(X: Integer; Y: Double); overload;
            procedure Run;
          end;
          TBoxH0 = class helper for TBox
            procedure Grow(const S: string); overload;
          end;
          TBoxH = class helper(TBoxH0) for TBox
            procedure Show(const S: string); overload;
            procedure Hide(N: Integer);
            procedure Same(N: Integer); overload;
            procedure Grow(D: Double); overload;
            procedure Amb(X: Double; Y: Integer); overload;
          end;
        procedure TBase.Show(N: Integer); begin end;
        procedure TBase.Grow(N: Integer); begin end;
        procedure TBox.Show; begin end;
        procedure TBox.Hide; begin end;
        procedure TBox.Same(N: Integer); begin end;
        procedure TBox.Grow(const S: string; N: Integer); begin end;
        procedure TBox.Amb(X: Integer; Y: Double); begin end;
        procedure TBoxH0.Grow(const S: string); begin end;
        procedure TBoxH.Show(const S: string); begin end;
        procedure TBoxH.Hide(N: Integer); begin end;
        procedure TBoxH.Same(N: Integer); begin end;
        procedure TBoxH.Grow(D: Double); begin end;
        procedure TBoxH.Amb(X: Double; Y: Integer); begin end;
        procedure TBox.Run;
        begin
          $IMPLICIT$;
        end;
        var
          B: TBox;
        begin
          B := nil;
          B.$CALL$;
        end.
        """;

    /// <summary>
    /// LANG-992 step 3: when the place found declares the name as methods that all carry <c>overload</c>, the candidates continue
    /// with the rest of the helper chain, the own methods of the level, then the next levels, until a place declares the name
    /// without <c>overload</c>; a method with the parameter types of a nearer candidate is not added. The same set for
    /// <c>B.M(…)</c> and for an unqualified call in a method of the class; E2110, E2122 and E2123 keep their texts.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-992")]
    [InlineData("class overload without argument", "Show", "TBox.Show")]
    [InlineData("helper overload chosen by the argument", "Show('ab')", "TBoxH.Show")]
    [InlineData("overload of the ancestor class", "Show(1)", "TBase.Show")]
    [InlineData("helper method without overload hides the class's", "Hide(1)", "TBoxH.Hide")]
    [InlineData("the hidden class method is no candidate", "Hide", "E2110: Not enough actual parameters for 'Hide'")]
    [InlineData("equal parameter types: the nearer one", "Same(1)", "TBoxH.Same")]
    [InlineData("the ancestor helper of the chain", "Grow('ab')", "TBoxH0.Grow")]
    [InlineData("chain, then the class", "Grow('ab', 1)", "TBox.Grow")]
    [InlineData("chain, class, then the ancestor class", "Grow(1)", "TBase.Grow")]
    [InlineData("the head of the chain", "Grow(1.5)", "TBoxH.Grow")]
    [InlineData("E2122 unchanged", "Amb(1, 1)", "E2122: Ambiguous overloaded call to 'Amb'")]
    [InlineData("E2123 unchanged", "Show(1, 2)", "E2123: There is no overloaded version of 'Show' that can be called with these arguments")]
    public void Lookup_OneOverloadSet(string what, string call, string expected)
    {
        var (result, _) = Bind(OverloadProgram.Replace("$IMPLICIT$", call, StringComparison.Ordinal).Replace("$CALL$", call, StringComparison.Ordinal));
        string Outcome(BoundStatement statement) =>
            result.Diagnostics.FirstOrDefault(d => d.Location.Span.Start >= statement.Syntax.Span.Start && d.Location.Span.Start < statement.Syntax.Span.End) is { } error
                ? $"{error.Code}: {error.Message}"
                : Called(statement);

        Assert.True(result.Diagnostics.All(d => d.Severity == DiagnosticSeverity.Error), what + ": " + string.Join(", ", Format(result.Diagnostics)));
        Assert.Equal(expected, Outcome(BodyOf(result, "TBox", "Run")[0]));                                  // unqualified, on Self
        Assert.Equal(expected, Outcome(result.Bodies[0].Statements[1]));                                    // B.M(…)
    }

    // ------------------------------------------------------------------ LANG-992 (c), (e): class references and helper names

    /// <summary>
    /// LANG-992 (c): on a class-reference value the static members of the helpers of the class and its ancestors are found; a
    /// non-static class method of a class helper gets the value as <c>Self</c>. LANG-992 (e): through the helper's own name its
    /// static class methods (a non-static one with the helped class as <c>Self</c>), class vars and class properties, also of a
    /// record helper; an instance member through either is E2607.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-992")]
    public void Lookup_ClassReferenceAndHelperName()
    {
        var (result, _) = Bind("""
            program P;
            type
              TBox = class
              end;
              TBig = class(TBox)
              end;
              TBoxClass = class of TBox;
              TBoxHelper = class helper for TBox
              private
                class var FTotal: Integer;
              public
                class var Count: Integer;
                class function Kind: string; static;
                class function Named: string;
                class property Total: Integer read FTotal write FTotal;
              end;
              TPair = record A, B: Integer; end;
              TPairHelper = record helper for TPair
                class function Make(X, Y: Integer): TPair; static;
              end;
            class function TBoxHelper.Kind: string; begin Result := 'kind'; end;
            class function TBoxHelper.Named: string; begin Result := 'named'; end;
            class function TPairHelper.Make(X, Y: Integer): TPair; begin Result.A := X; Result.B := Y; end;
            var
              C: TBoxClass;
              S: string;
              N: Integer;
              Pr: TPair;
            begin
              C := TBig;
              S := C.Kind;
              S := C.Named;
              S := TBoxHelper.Kind;
              S := TBoxHelper.Named;
              N := TBoxHelper.Count;
              N := C.Count;
              N := TBoxHelper.Total;
              N := C.Total;
              Pr := TPairHelper.Make(1, 2);
            end.
            """);

        Assert.Equal([], Format(result.Diagnostics));
        var statements = result.Bodies[0].Statements;
        string ReceiverOf(BoundStatement statement)
        {
            var call = Assert.IsType<BoundCallExpression>(((BoundAssignmentStatement)statement).Value);
            var self = call.Receiver?.Self switch
            {
                BoundClassReferenceExpression reference => "class " + reference.Class.Name,
                BoundVariableExpression variable => variable.Variable.Name,
                null => "static",
                var other => BoundDump.Of(other),
            };
            return $"{Called(statement)} {self}";
        }

        Assert.Equal(["TBoxHelper.Kind static", "TBoxHelper.Named C", "TBoxHelper.Kind static", "TBoxHelper.Named class TBox"],
            statements.Skip(1).Take(4).Select(ReceiverOf));
        Assert.Equal(["(:= N:Integer Count:Integer)", "(:= N:Integer Count:Integer)"], statements.Skip(5).Take(2).Select(BoundDump.Of));
        Assert.All(statements.Skip(7).Take(2), s => Assert.False(((BoundAssignmentStatement)s).Value.HasErrors));
        Assert.Equal("TPairHelper.Make static", ReceiverOf(statements[9]));

        Assert.Equal([
            "E2607(15,8): Constructor or class method expected for a call on a class reference",
            "E2607(16,8): Constructor or class method expected for a call on a class reference",
            "E2607(17,10): Constructor or class method expected for a call on a class reference",
            "E2607(18,19): Constructor or class method expected for a call on a class reference",
        ], BindDiagnostics("""
            program P;
            type
              TBox = class end;
              TBoxClass = class of TBox;
              TBoxHelper = class helper for TBox
                function Inst: string;
                property Prop: string read Inst;
              end;
            function TBoxHelper.Inst: string; begin Result := 'inst'; end;
            var
              C: TBoxClass;
              S: string;
            begin
              C := TBox;
              S := C.Inst;
              S := TBoxHelper.Inst;
              S := C.Prop;
              S := TBoxHelper.Prop;
            end.
            """));
    }

    // ------------------------------------------------------------------ LANG-992 step 4: no member

    /// <summary>
    /// LANG-992 step 4: no place declares the name — E2005 with the name of the type when the type has an active helper, also for a
    /// type without members of its own; E2006 at the receiver when it has neither members nor an active helper. A pointer to a
    /// record finds the helper of the record (<c>Q.Sum</c> as <c>Q^.Sum</c>).
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-992")]
    public void Lookup_MissingMember()
    {
        var (result, _) = Bind("""
            program P;
            type
              TIntH = record helper for Integer
                function Twice: Integer;
              end;
              TBox = class end;
              TBoxH = class helper for TBox
                function Twice: Integer;
              end;
              TPair = record A, B: Integer; end;
              TPairH = record helper for TPair
                function Sum: Integer;
              end;
              PPair = ^TPair;
            function TIntH.Twice: Integer; begin Result := Self * 2; end;
            function TBoxH.Twice: Integer; begin Result := 2; end;
            function TPairH.Sum: Integer; begin Result := A + B; end;
            var
              N: Integer;
              D: Double;
              B: TBox;
              Q: PPair;
            begin
              N := 1; D := 1; B := nil; Q := nil;
              N := N.Missing;
              D := D.Missing;
              N := B.Missing;
              N := Q.Sum;
              N := Q.Missing;
            end.
            """);

        Assert.Equal([
            "E2005(25,10): 'Integer' does not contain a member named 'Missing'",
            "E2006(26,8): 'D' is not a unit, record, class or interface; cannot qualify with '.'",
            "E2005(27,10): 'TBox' does not contain a member named 'Missing'",
            "E2005(29,10): 'TPair' does not contain a member named 'Missing'",
        ], Format(result.Diagnostics));
        Assert.Equal("TPairH.Sum", Called(result.Bodies[0].Statements[7]));
    }

    // ------------------------------------------------------------------ LANG-992 (f): helper methods as values

    /// <summary>
    /// LANG-992 (f): a static class method of a helper is a plain procedural value (through the helped type and through the
    /// helper's name, also of a record helper); an instance method and a non-static class method of a class helper are method
    /// pointers (data = the object, or the class); an instance method of a record helper is no value (E2900).
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-992")]
    public void MethodValues()
    {
        var (result, _) = Bind("""
            program P;
            type
              TBox = class
                function Size: Integer;
              end;
              TBoxHelper = class helper for TBox
                function Twice: Integer;
                class function Kind: string; static;
                class function Named: string;
              end;
              TPair = record A, B: Integer; end;
              TPairHelper = record helper for TPair
                class function Zero: Integer; static;
              end;
              TIntMethod = function: Integer of object;
              TStrPlain = function: string;
              TIntPlain = function: Integer;
              TStrMethod = function: string of object;
            function TBox.Size: Integer; begin Result := 1; end;
            function TBoxHelper.Twice: Integer; begin Result := Size * 2; end;
            class function TBoxHelper.Kind: string; begin Result := 'kind'; end;
            class function TBoxHelper.Named: string; begin Result := 'named'; end;
            class function TPairHelper.Zero: Integer; begin Result := 0; end;
            var
              B: TBox;
              F: TIntMethod;
              G: TStrPlain;
              Z: TIntPlain;
              M: TStrMethod;
            begin
              B := nil;
              G := TBox.Kind;
              G := @TBox.Kind;
              G := TBoxHelper.Kind;
              Z := TPair.Zero;
              Z := TPairHelper.Zero;
              F := B.Twice;
              F := @B.Twice;
              M := TBox.Named;
              M := B.Named;
            end.
            """);

        Assert.Equal([], Format(result.Diagnostics));
        static string Value(BoundNode node) => node switch
        {
            BoundAssignmentStatement assignment => Value(assignment.Value),
            BoundConversionExpression conversion => Value(conversion.Operand),
            BoundAddressOfExpression { Routine: { } routine } => $"address {routine.ContainingSymbol!.Name}.{routine.Name}",
            BoundMethodPointerExpression pointer => $"method {pointer.Method.ContainingSymbol!.Name}.{pointer.Method.Name} on " + pointer.Self switch
            {
                BoundClassReferenceExpression reference => "class " + reference.Class.Name,
                BoundVariableExpression variable => variable.Variable.Name,
                BoundDereferenceExpression { Pointer: BoundVariableExpression variable } => "class of " + variable.Variable.Name,
                var other => BoundDump.Of(other),
            },
            _ => BoundDump.Of(node),
        };
        Assert.Equal([
            "address TBoxHelper.Kind", "address TBoxHelper.Kind", "address TBoxHelper.Kind", "address TPairHelper.Zero", "address TPairHelper.Zero",
            "method TBoxHelper.Twice on B", "method TBoxHelper.Twice on B", "method TBoxHelper.Named on class TBox", "method TBoxHelper.Named on class of B",
        ], result.Bodies[0].Statements.Skip(1).Select(Value));

        Assert.Equal([
            "E2900(18,8): Feature not supported: method value of a record-helper method",
            "E2900(19,8): Feature not supported: method value of a record-helper method",
            "E2900(20,8): Feature not supported: method value of a record-helper method",
            "E2900(21,8): Feature not supported: method value of a record-helper method",
        ], BindDiagnostics("""
            program P;
            type
              TPair = record A, B: Integer; end;
              TPairHelper = record helper for TPair
                function Sum: Integer;
              end;
              TIntH = record helper for Integer
                function Twice: Integer;
              end;
              TIntMethod = function: Integer of object;
            function TPairHelper.Sum: Integer; begin Result := A + B; end;
            function TIntH.Twice: Integer; begin Result := Self * 2; end;
            var
              Pr: TPair;
              N: Integer;
              F: TIntMethod;
            begin
              F := Pr.Sum;
              F := @Pr.Sum;
              F := N.Twice;
              F := @N.Twice;
            end.
            """));
    }

    // ------------------------------------------------------------------ LANG-994: H2012

    private const string HidingProgram = """
        $DIRECTIVE$
        program P;
        type
          TBase = class
            function Inherit: string;
          end;
          TBox = class(TBase)
          strict private
            procedure Secret;
          public
            class var Total: Integer;
            function Name: string;
            function Value: Integer;
            procedure Over(N: Integer); overload;
          end;
          TBaseH = class helper for TBase
            function Extra: string;
          end;
          TBoxH0 = class helper for TBox
            function Chained: string;
          end;
          TPair = record A: Integer; end; TColor = (Red, Blue);
          TH = $HEADER$
            $MEMBER$
          end;
        procedure TBox.Secret; begin end;
        function TBase.Inherit: string; begin Result := ''; end;
        function TBox.Name: string; begin Result := ''; end;
        function TBox.Value: Integer; begin Result := 0; end;
        procedure TBox.Over(N: Integer); begin end;
        function TBaseH.Extra: string; begin Result := ''; end;
        function TBoxH0.Chained: string; begin Result := ''; end;
        $BODY$
        begin
        end.
        """;

    /// <summary>
    /// LANG-994: a helper member that hides a member of the helped type (found by LANG-992 steps 1…2 without the helper's own
    /// chain, among the members the helper may use) gets H2012 at its name, with the qualified name of the hidden member — unless
    /// it is a method with <c>overload</c> and other parameter types (an added overload). A member of the helper's own chain, a
    /// built-in helped type without members, an element of a helped enumeration (it comes first) and a member the helper may not
    /// use give none; the hint obeys
    /// <c>{$WARN HELPER_HIDES_MEMBER}</c> and <c>{$HINTS}</c>.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-994")]
    [InlineData("method without overload", "", "class helper for TBox", "function Name: string;", "function TH.Name: string; begin Result := ''; end;", 14, "TBox.Name")]
    [InlineData("property over a method", "", "class helper for TBox", "function GetV: Integer; property Value: Integer read GetV;",
        "function TH.GetV: Integer; begin Result := 0; end;", 38, "TBox.Value")]
    [InlineData("constant over a method", "", "class helper for TBox", "const Name = 1;", "", 11, "TBox.Name")]
    [InlineData("class var over a class var", "", "class helper for TBox", "class var Total: Integer;", "", 15, "TBox.Total")]
    [InlineData("same parameter types with overload", "", "class helper for TBox", "procedure Over(N: Integer); overload;",
        "procedure TH.Over(N: Integer); begin end;", 15, "TBox.Over")]
    [InlineData("an added overload", "", "class helper for TBox", "procedure Over(const S: string); overload;",
        "procedure TH.Over(const S: string); begin end;", 0, null)]
    [InlineData("inherited member, named with its class", "", "class helper for TBox", "function Inherit: string;",
        "function TH.Inherit: string; begin Result := ''; end;", 14, "TBase.Inherit")]
    [InlineData("member of the helper of an ancestor level", "", "class helper for TBox", "function Extra: string;",
        "function TH.Extra: string; begin Result := ''; end;", 14, "TBaseH.Extra")]
    [InlineData("member of an ancestor helper of the own chain", "", "class helper(TBoxH0) for TBox", "function Chained: string;",
        "function TH.Chained: string; begin Result := ''; end;", 0, null)]
    [InlineData("built-in helped type", "", "record helper for Integer", "function Twice: Integer;",
        "function TH.Twice: Integer; begin Result := Self * 2; end;", 0, null)]
    [InlineData("a field of the helped record", "", "record helper for TPair", "function A: Integer;",
        "function TH.A: Integer; begin Result := 0; end;", 14, "TPair.A")]
    [InlineData("an element of a helped enumeration (found before the helper's members)", "", "record helper for TColor", "const Red = 5;", "", 0, null)]
    [InlineData("a member the helper may not use", "", "class helper for TBox", "procedure Secret;", "procedure TH.Secret; begin end;", 0, null)]
    [InlineData("WARN HELPER_HIDES_MEMBER OFF", "{$WARN HELPER_HIDES_MEMBER OFF}", "class helper for TBox", "function Name: string;",
        "function TH.Name: string; begin Result := ''; end;", 0, null)]
    [InlineData("HINTS OFF", "{$HINTS OFF}", "class helper for TBox", "function Name: string;",
        "function TH.Name: string; begin Result := ''; end;", 0, null)]
    public void HidesMember_Hint(string what, string directive, string header, string member, string body, int column, string? hidden)
    {
        var (result, _) = Bind(HidingProgram.Replace("$DIRECTIVE$", directive, StringComparison.Ordinal)
            .Replace("$HEADER$", header, StringComparison.Ordinal).Replace("$MEMBER$", member, StringComparison.Ordinal)
            .Replace("$BODY$", body, StringComparison.Ordinal));
        var name = hidden?[(hidden.IndexOf('.') + 1)..];
        Assert.True(result.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error), what + ": " + string.Join(", ", Format(result.Diagnostics)));
        Assert.Equal(hidden is null ? [] : [$"H2012(24,{column}): Helper member '{name}' hides '{hidden}'"],
            Format(result.Diagnostics.Where(d => d.Code == "H2012")));
    }
}
