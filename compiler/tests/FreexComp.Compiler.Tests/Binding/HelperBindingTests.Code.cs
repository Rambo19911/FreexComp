// The diagnostics of an in-memory compilation are read synchronously; the optional token is left out on purpose.
#pragma warning disable xUnit1051

using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

// LANG-990 (a)…(c), LANG-991: the code of a helper (phase8/helpers-completion.md §15.2; card F10-027), C-439 — visibility from
// helper code, inherited, the Self of class methods of class helpers, the scope inside helper methods, rejected declarations.
public sealed partial class HelperBindingTests
{
    // ------------------------------------------------------------------ LANG-991 (c): visibility

    private const string VisibilityClassUnit = """
        unit UAcc;
        interface
        type
          TAcc = class
          strict private
            FSP: Integer;
          private
            FP: Integer;
          strict protected
            FSQ: Integer;
          protected
            FQ: Integer;
            procedure Changed;
          public
            FPub: Integer;
            procedure Touch;
          end;
          TAccRec = record
          strict private
            RSP: Integer;
          private
            RP: Integer;
          public
            RPub: Integer;
            procedure Touch;
          end;
        implementation
        procedure TAcc.Changed;
        begin
        end;
        procedure TAcc.Touch;
        begin
          FSP := FP + FSQ + FQ;
          Changed;
        end;
        procedure TAccRec.Touch;
        begin
          RSP := RP;
        end;
        end.
        """;

    private const string VisibilityHelperUnit = """
        unit UH;
        interface
        uses UAcc;
        type
          THBase = class helper for TAcc
          strict private
            procedure SPriv;
          strict protected
            procedure SProt;
          private
            procedure Priv;
          protected
            procedure Prot;
          public
            procedure Pub;
          end;
        procedure InUnit(A: TAcc);
        implementation
        procedure THBase.SPriv;
        begin
        end;
        procedure THBase.SProt;
        begin
        end;
        procedure THBase.Priv;
        begin
        end;
        procedure THBase.Prot;
        begin
        end;
        procedure THBase.Pub;
        begin
          SPriv;
          SProt;
          Priv;
          Prot;
        end;
        procedure InUnit(A: TAcc);
        begin
          $U$;
        end;
        end.
        """;

    private const string VisibilityProgram = """
        program P;
        uses UAcc, UH;
        type
          TLocal = class
          strict private
            FSP: Integer;
          private
            FP: Integer;
          strict protected
            FSQ: Integer;
          protected
            FQ: Integer;
          public
            procedure Touch;
          end;
          TAccH = class helper for TAcc
            procedure Run(Other: TAcc);
          end;
          TLocalH = class helper for TLocal
            procedure Run(Other: TLocal);
          end;
          TRecH = record helper for TAccRec
            procedure Run(const Other: TAccRec);
          end;
          THDerived = class helper(THBase) for TAcc
            procedure Run2;
          end;
        var
          N: Integer;
        procedure TLocal.Touch;
        begin
          FSP := FP + FSQ + FQ;
        end;
        procedure TAccH.Run(Other: TAcc);
        begin
          $A$;
        end;
        procedure TLocalH.Run(Other: TLocal);
        begin
          $L$;
        end;
        procedure TRecH.Run(const Other: TAccRec);
        begin
          $R$;
        end;
        procedure THDerived.Run2;
        begin
          $D$;
        end;
        procedure Outside(A: TAcc);
        begin
          $M$;
        end;
        begin
          Outside(nil);
          Writeln(N);
        end.
        """;

    /// <summary>
    /// The rows of <see cref="Visibility_FromHelperCode"/>: what is tested, the slot (<c>A</c> a class helper for a class of another
    /// unit, <c>L</c> for a class of the same unit, <c>R</c> a record helper for a record of another unit, <c>D</c> a helper of
    /// another unit inheriting from <c>THBase</c>, <c>U</c> other code of <c>THBase</c>'s unit, <c>M</c> code of another unit) and
    /// the statement.
    /// </summary>
    public static TheoryData<string, string, string> VisibilityRows() => new()
    {
        // members of a class of another unit, from a helper for it (a descendant declared in the helper's unit)
        { "other unit: public, unqualified", "A", "N := FPub" },
        { "other unit: protected, unqualified", "A", "N := FQ" },
        { "other unit: protected, through Self", "A", "N := Self.FQ" },
        { "other unit: protected, through another receiver of the class", "A", "N := Other.FQ" },
        { "other unit: a protected method, unqualified", "A", "Changed" },
        { "other unit: a protected method, through another receiver", "A", "Other.Changed" },
        { "other unit: strict protected, unqualified", "A", "N := FSQ" },
        { "other unit: strict protected, through Self", "A", "N := Self.FSQ" },
        { "other unit: strict protected, through another receiver", "A", "N := Other.FSQ" },
        { "other unit: private, unqualified", "A", "N := FP{!E2610}" },
        { "other unit: private, through Self", "A", "N := Self.FP{!E2610}" },
        { "other unit: private, through another receiver", "A", "N := Other.FP{!E2610}" },
        { "other unit: strict private, unqualified", "A", "N := FSP{!E2610}" },
        { "other unit: strict private, through Self", "A", "N := Self.FSP{!E2610}" },
        { "other unit: strict private, through another receiver", "A", "N := Other.FSP{!E2610}" },

        // members of a class of the same unit
        { "same unit: private, unqualified", "L", "N := FP" },
        { "same unit: private, through another receiver", "L", "N := Other.FP" },
        { "same unit: protected, through Self", "L", "N := Self.FQ" },
        { "same unit: strict protected, unqualified", "L", "N := FSQ" },
        { "same unit: strict protected, through another receiver", "L", "N := Other.FSQ" },
        { "same unit: strict private, unqualified", "L", "N := FSP{!E2610}" },
        { "same unit: strict private, through another receiver", "L", "N := Other.FSP{!E2610}" },

        // members of a record of another unit, from a record helper for it
        { "record: public, unqualified", "R", "N := RPub" },
        { "record: public, through another receiver", "R", "N := Other.RPub" },
        { "record: private, unqualified", "R", "N := RP{!E2610}" },
        { "record: private, through another receiver", "R", "N := Other.RP{!E2610}" },
        { "record: strict private, unqualified", "R", "N := RSP{!E2610}" },
        { "record: strict private, through Self", "R", "N := Self.RSP{!E2610}" },

        // the helper's own members, from a helper of another unit that inherits from it
        { "inheriting helper: strict protected, unqualified", "D", "SProt" },
        { "inheriting helper: strict protected, through Self", "D", "Self.SProt" },
        { "inheriting helper: protected, unqualified", "D", "Prot" },
        { "inheriting helper: public, unqualified", "D", "Pub" },
        { "inheriting helper: strict private, unqualified", "D", "SPriv{!E2610}" },
        { "inheriting helper: private of another unit, unqualified", "D", "Priv{!E2610}" },

        // … from other code of the helper's unit
        { "helper's unit: private", "U", "A.Priv" },
        { "helper's unit: protected", "U", "A.Prot" },
        { "helper's unit: public", "U", "A.Pub" },
        { "helper's unit: strict protected", "U", "A{!E2610}.SProt" },
        { "helper's unit: strict private", "U", "A{!E2610}.SPriv" },

        // … from code of another unit
        { "another unit: public", "M", "A.Pub" },
        { "another unit: private", "M", "A{!E2610}.Priv" },
        { "another unit: protected", "M", "A{!E2610}.Prot" },
        { "another unit: strict protected", "M", "A{!E2610}.SProt" },
    };

    /// <summary>
    /// LANG-991 (c): code of a helper for <c>T</c> counts as code of a descendant of <c>T</c> declared in the helper's unit —
    /// <c>protected</c> and <c>strict protected</c> members of <c>T</c> are usable unqualified, on <c>Self</c> and on another
    /// receiver of <c>T</c>; <c>private</c> ones only in <c>T</c>'s unit; <c>strict private</c> ones never (E2610). The same for a
    /// record. For a helper's own members the helper is the type: <c>strict protected</c> opens to helpers that inherit from it,
    /// <c>private</c> and <c>protected</c> to the code of its unit; code elsewhere sees <c>public</c> members only.
    /// </summary>
    [Theory]
    [MemberData(nameof(VisibilityRows))]
    [Trait("Req", "LANG-991")]
    public void Visibility_FromHelperCode(string what, string slot, string statement)
    {
        _ = what;
        string Fill(string text)
        {
            foreach (var other in new[] { "A", "L", "R", "D", "U", "M" })
            {
                var empty = other is "A" or "L" or "R" ? "N := 1" : "Writeln";
                text = text.Replace($"${other}$", other == slot ? statement : empty, StringComparison.Ordinal);
            }

            return text.ReplaceLineEndings("\n");
        }

        AssertMarkers([("UAcc.pas", VisibilityClassUnit), ("UH.pas", Fill(VisibilityHelperUnit)), ("P.pas", Fill(VisibilityProgram))]);
    }

    // ------------------------------------------------------------------ LANG-991 (d): inherited

    /// <summary>The statements of the body of the method <paramref name="owner"/>.<paramref name="method"/>.</summary>
    private static IReadOnlyList<BoundStatement> BodyOf(BindingResult result, string owner, string method) =>
        result.Routines.Single(r => r.Routine.Name == method && r.Routine.ContainingSymbol?.Name == owner).Body.Statements;

    /// <summary>"Owner.Method virtual|static" of a call statement, or of the call an assignment holds.</summary>
    private static string CallOf(BoundStatement statement)
    {
        BoundNode node = statement is BoundAssignmentStatement { Value: var value } ? value : statement;
        while (node is BoundConversionExpression conversion)
        {
            node = conversion.Operand;
        }

        var (routine, receiver) = node switch
        {
            BoundCallStatement call => (call.Routine, call.Receiver),
            BoundCallExpression call => (call.Routine, call.Receiver),
            _ => throw new InvalidOperationException($"no call: {BoundDump.Of(node)}"),
        };
        return $"{routine.ContainingSymbol!.Name}.{routine.Name} {(receiver?.IsVirtual == true ? "virtual" : "static")}";
    }

    /// <summary>
    /// LANG-991 (d): in a method of helper <c>H</c>, <c>inherited Name</c>, a bare <c>inherited</c> and <c>inherited Prop</c>
    /// resolve on <c>Self</c> starting after <c>H</c> — the rest of <c>H</c>'s chain first, then the helped type's own members;
    /// a virtual method of the class stays a virtual call (dispatched on the object's class); a bare <c>inherited</c> without a
    /// member of the method's name and parameters calls nothing and reports nothing; the same in a record helper.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-991")]
    public void Inherited_ReachesTheHiddenMember()
    {
        var (result, _) = Bind("""
            program P;
            type
              TBase = class
                function Name: string; virtual;
                procedure Show; virtual;
                procedure Plain(N: Integer);
                function GetTitle: string; virtual;
                property Title: string read GetTitle;
              end;
              TBaseH = class helper for TBase
                function Name: string;
              end;
              TBaseH2 = class helper(TBaseH) for TBase
                function Name: string;
                procedure Show;
                procedure Plain(N: Integer);
                procedure Lonely;
                function Title: string;
              end;
              TRec = record
                X: Integer;
                procedure Reset;
              end;
              TRecH = record helper for TRec
                procedure Reset;
              end;
              TIntH = record helper for Integer
                procedure Bump;
              end;
            function TBase.Name: string; begin Result := 'base'; end;
            procedure TBase.Show; begin end;
            procedure TBase.Plain(N: Integer); begin end;
            function TBase.GetTitle: string; begin Result := 'title'; end;
            function TBaseH.Name: string;
            begin
              Result := inherited Name;
            end;
            function TBaseH2.Name: string;
            begin
              Result := inherited Name;
            end;
            procedure TBaseH2.Show;
            begin
              inherited;
            end;
            procedure TBaseH2.Plain(N: Integer);
            begin
              inherited;
              inherited Plain(N + 1);
            end;
            procedure TBaseH2.Lonely;
            begin
              inherited;
            end;
            function TBaseH2.Title: string;
            begin
              Result := inherited Title;
            end;
            procedure TRec.Reset; begin X := 0; end;
            procedure TRecH.Reset;
            begin
              inherited Reset;
              inherited;
            end;
            procedure TIntH.Bump;
            begin
              inherited;
            end;
            begin
            end.
            """);

        Assert.Equal([], Format(result.Diagnostics.Where(d => d.Code != "H2012")));                // these helpers hide on purpose (LANG-994, C-440)
        Assert.Equal(["TBase.Name virtual"], BodyOf(result, "TBaseH", "Name").Select(CallOf));
        Assert.Equal(["TBaseH.Name static"], BodyOf(result, "TBaseH2", "Name").Select(CallOf));             // the chain first
        Assert.Equal(["TBase.Show virtual"], BodyOf(result, "TBaseH2", "Show").Select(CallOf));
        Assert.Equal(["TBase.Plain static", "TBase.Plain static"], BodyOf(result, "TBaseH2", "Plain").Select(CallOf));
        var passed = Assert.IsType<BoundCallStatement>(BodyOf(result, "TBaseH2", "Plain")[0]).Arguments.Single();
        Assert.Equal("N", Assert.IsType<BoundParameterExpression>(passed).Parameter.Name);
        Assert.Equal(["TBase.GetTitle virtual"], BodyOf(result, "TBaseH2", "Title").Select(CallOf));
        Assert.Equal(["TRec.Reset static", "TRec.Reset static"], BodyOf(result, "TRecH", "Reset").Select(CallOf));
        Assert.All(new[] { BodyOf(result, "TBaseH2", "Lonely"), BodyOf(result, "TIntH", "Bump") },
            body => Assert.Equal("(block)", BoundDump.Of(Assert.Single(body))));                         // nothing to call
    }

    // ------------------------------------------------------------------ LANG-991 (a): Self of class methods

    /// <summary>
    /// LANG-991 (a): a class method of a class helper without <c>static</c> has <c>Self</c> of type <c>class of T</c> — the class it
    /// is called on (<c>T.M</c>, <c>TChild.M</c>), the class of the object (<c>Obj.M</c>), the current class in an unqualified
    /// call from an instance or class method of the helper; with <c>static</c> it has none, nor have class methods of record
    /// helpers. An instance member used without an object in such a class method is E2608.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-991")]
    public void ClassMethod_SelfIsTheClassReference()
    {
        var (result, _) = Bind("""
            program P;
            type
              TAcc = class
                F: Integer;
                class function Title: string; virtual;
              end;
              TSav = class(TAcc)
              end;
              TAccH = class helper for TAcc
                class function Kind: string;
                class function Fixed: string; static;
                function Inst: string;
                class function Inner: string;
              end;
              TIntH = record helper for Integer
                class function Zero: Integer;
              end;
            class function TAcc.Title: string;
            begin
              Result := 'acc';
            end;
            class function TAccH.Kind: string;
            begin
              Result := Self.Title;
            end;
            class function TAccH.Fixed: string;
            begin
              Result := 'fixed';
            end;
            function TAccH.Inst: string;
            begin
              Result := Kind;
            end;
            class function TAccH.Inner: string;
            begin
              Result := Kind;
            end;
            class function TIntH.Zero: Integer;
            begin
              Result := 0;
            end;
            var
              A: TAcc;
              S: string;
              N: Integer;
            begin
              S := TAcc.Kind;
              S := TSav.Kind;
              S := A.Kind;
              S := TAcc.Fixed;
              N := Integer.Zero;
            end.
            """);

        Assert.Equal([], Format(result.Diagnostics));
        MethodSymbol Method(string owner, string name) => (MethodSymbol)result.Routines.Single(r => r.Routine.Name == name && r.Routine.ContainingSymbol?.Name == owner).Routine;
        var kind = Method("TAccH", "Kind");
        Assert.Equal(MethodKind.ClassMethod, kind.MethodKind);
        Assert.Equal("TAcc", Assert.IsType<ClassRefTypeSymbol>(kind.SelfParameter!.Type).Class.Name);
        Assert.Null(Method("TAccH", "Fixed").SelfParameter);
        Assert.Null(Method("TIntH", "Zero").SelfParameter);

        string ReceiverOf(BoundStatement statement)
        {
            var call = Assert.IsType<BoundCallExpression>(((BoundAssignmentStatement)statement).Value);
            return call.Receiver switch
            {
                { Self: BoundClassReferenceExpression reference } => "class " + reference.Class.Name,
                { Self: BoundDereferenceExpression { Pointer: BoundVariableExpression v } } => "class of " + v.Variable.Name,
                { Self: BoundDereferenceExpression { Pointer: BoundParameterExpression p } } => "class of " + p.Parameter.Name,
                { Self: BoundParameterExpression p } => p.Parameter.Name + ": " + p.Type.Name,
                { Self: null } => "static",
                var other => other?.ToString() ?? "none",
            };
        }

        Assert.Equal(["class TAcc", "class TSav", "class of A", "static", "static"], result.Bodies[0].Statements.Select(ReceiverOf));
        Assert.Equal(["class of Self"], BodyOf(result, "TAccH", "Inst").Select(ReceiverOf));
        Assert.Equal(["Self: class of TAcc"], BodyOf(result, "TAccH", "Inner").Select(ReceiverOf));

        Assert.Equal(["E2608(5,52): Instance member 'F' is not accessible from a class method"], BindDiagnostics("""
            program P;
            type
              TAcc = class F: Integer; end;
              TAccH = class helper for TAcc class function Bad: Integer; end;
            class function TAccH.Bad: Integer; begin Result := F; end;
            begin
            end.
            """));
    }

    // ------------------------------------------------------------------ LANG-991 (b): scope

    /// <summary>
    /// LANG-991 (b): an unqualified name in a method of helper <c>H</c> for <c>T</c> is a member of <c>Self</c> with <c>H</c> as the
    /// active helper of <c>T</c> (also where another helper of <c>T</c> is nearer), then — level by level — the members of the active
    /// helper of an ancestor of <c>T</c> before that ancestor's own members.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-991")]
    public void Scope_InsideHelperMethods()
    {
        var (result, _) = Bind("""
            program P;
            type
              TBase = class
                function Who: string;
                function Plain: string;
              end;
              TMid = class(TBase)
              end;
              TBaseH = class helper for TBase
                function FromBaseHelper: string;
                function Who: string;
              end;
              TMidH = class helper for TMid
                function Run: string;
                function Name: string;
              end;
              TMidH2 = class helper for TMid
                function Name: string;
              end;
            function TBase.Who: string; begin Result := 'base'; end;
            function TBase.Plain: string; begin Result := 'plain'; end;
            function TBaseH.FromBaseHelper: string; begin Result := 'from'; end;
            function TBaseH.Who: string; begin Result := 'helper'; end;
            function TMidH.Name: string; begin Result := 'mid'; end;
            function TMidH2.Name: string; begin Result := 'mid2'; end;
            function TMidH.Run: string;
            begin
              Result := Name;
              Result := FromBaseHelper;
              Result := Who;
              Result := Plain;
            end;
            begin
            end.
            """);

        Assert.Equal([], Format(result.Diagnostics.Where(d => d.Code != "H2012")));                // these helpers hide on purpose (LANG-994, C-440)
        Assert.Equal(["TMidH.Name static", "TBaseH.FromBaseHelper static", "TBaseH.Who static", "TBase.Plain static"],
            BodyOf(result, "TMidH", "Run").Select(CallOf));
    }

    // ------------------------------------------------------------------ LANG-990 (a)…(c): rejected declarations

    /// <summary>
    /// LANG-990 (a)…(c): a constructor in a helper, a <c>default</c> array property, a class helper for a type that is no class, a
    /// record helper for a class, for an interface and for a dispatch interface, and a helper nested in a type are rejected.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-990")]
    [InlineData("TH = class helper for TFoo constructor Create; end;", "", "E2900(9,30): Feature not supported: helper constructor")]
    [InlineData("TH = class helper for TFoo function GetItem(I: Integer): Integer; property Items[I: Integer]: Integer read GetItem; default; end;",
        "function TH.GetItem(I: Integer): Integer; begin Result := I; end;", "E2900(9,119): Feature not supported: default property in a helper")]
    [InlineData("TH = class helper for Integer end;", "", "E2603(9,25): Class type required")]
    [InlineData("TH = record helper for TFoo end;", "", "E2900(9,26): Feature not supported: record helper for a class")]
    [InlineData("TH = record helper for IFoo end;", "", "E2900(9,26): Feature not supported: helper for an interface type")]
    [InlineData("TH = record helper for DFoo end;", "", "E2900(9,26): Feature not supported: helper for an interface type")]
    [InlineData("TOuter = class type TH = class helper for TFoo end; end;", "", "E2900(9,28): Feature not supported: local helper type")]
    public void Declaration_Rejected(string declaration, string implementation, string expected)
    {
        Assert.Equal([expected], BindDiagnostics($"""
            program P;
            type
              TFoo = class
              end;
              IFoo = interface
              end;
              DFoo = dispinterface
              end;
              {declaration}
            {implementation}
            begin
            end.
            """));
    }
}
