using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-374 (generics.md §14.2): class constructors and class destructors of classes and records — declaration (LANG-786), never found
/// by lookup (LANG-787), implementation and body (LANG-788), diagnostics E2665…E2668.
/// </summary>
[Trait("Phase", "8")]
public sealed class ClassConstructorBindingTests
{
    private static T Lookup<T>(BindingResult result, string name) => Assert.IsType<T>(result.Declarations.Scope.Lookup(name));

    private static string[] Errors(string text) => BindDiagnostics(text);

    [Fact]
    [Trait("Req", "LANG-786")]
    [Trait("Req", "CMP-1395")]
    public void Declaration_ClassRecordGenericNested()
    {
        var (result, _) = Bind("""
            unit U;
            interface
            type
              TA = class
              private
                class constructor Create;
              public
                class destructor Destroy;
              type
                TN = class
                  class constructor Init;
                end;
              end;
              TR = record
                X: Integer;
                class constructor Create;
                class destructor Done;
              end;
              TBox<T> = class
                class var Size: Integer;
                class constructor Create;
                class destructor Destroy;
              end;
              TF = class;
              TF = class
                class destructor Destroy;
              end;
            var B: TBox<Integer>;
            implementation
            class constructor TA.Create; begin end;
            class destructor TA.Destroy; begin end;
            class constructor TA.TN.Init; begin end;
            class constructor TR.Create; begin end;
            class destructor TR.Done; begin end;
            class constructor TBox<T>.Create; begin Size := SizeOf(T) end;
            class destructor TBox<T>.Destroy; begin end;
            class destructor TF.Destroy; begin end;
            end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var a = Lookup<ClassTypeSymbol>(result, "TA");
        Assert.Equal(("Create", MethodKind.ClassConstructor, Visibility.Private), (a.ClassConstructor!.Name, a.ClassConstructor.MethodKind, a.ClassConstructor.Visibility));
        Assert.Equal(("Destroy", MethodKind.ClassDestructor, Visibility.Public), (a.ClassDestructor!.Name, a.ClassDestructor.MethodKind, a.ClassDestructor.Visibility));
        Assert.Null(a.ClassConstructor.SelfParameter);
        Assert.Empty(a.ClassConstructor.Parameters);
        var n = Assert.IsType<ClassTypeSymbol>(TypeMembers.NestedType(a, "TN"));
        Assert.Equal("Init", n.ClassConstructor!.Name);
        Assert.Null(n.ClassDestructor);
        var r = Lookup<RecordTypeSymbol>(result, "TR");
        Assert.Equal(("Create", "Done"), (r.ClassConstructor!.Name, r.ClassDestructor!.Name));
        var f = Lookup<ClassTypeSymbol>(result, "TF");
        Assert.Equal(MethodKind.ClassDestructor, f.ClassDestructor!.MethodKind);
        Assert.Null(f.ClassConstructor);

        // a generic binds them per instance (LANG-711); every body is bound
        var box = Assert.IsType<ClassTypeSymbol>(Lookup<VariableSymbol>(result, "B").Type);
        Assert.NotNull(box.ClassConstructor!.Definition);
        Assert.NotNull(box.ClassDestructor!.Definition);
        MethodSymbol[] all = [a.ClassConstructor, a.ClassDestructor, n.ClassConstructor, r.ClassConstructor, r.ClassDestructor, f.ClassDestructor,
            box.ClassConstructor, box.ClassDestructor];
        Assert.All(all, m => Assert.Contains(result.Routines, b => ReferenceEquals(b.Routine, m)));
        Assert.Equal("class constructor TA.Create", a.ClassConstructor.ToDisplayString());
        Assert.Equal("class destructor TR.Done", r.ClassDestructor.ToDisplayString());
    }

    /// <summary>LANG-787: <c>TFoo.Create</c> calls the ordinary constructor — its own, or the inherited one; the class constructor is no member.</summary>
    [Fact]
    [Trait("Req", "LANG-787")]
    public void NotFoundByLookup_OrdinaryConstructorCalled()
    {
        var (result, _) = Bind("""
            type
              TObject = class constructor Create; end;
              TA = class(TObject) class constructor Create; end;
              TB = class(TObject) class constructor Create; constructor Create(X: Integer); end;
            constructor TObject.Create; begin end;
            class constructor TA.Create; begin end;
            class constructor TB.Create; begin end;
            constructor TB.Create(X: Integer); begin end;
            var A: TA; B: TB;
            begin
              A := TA.Create;
              B := TB.Create(1);
            end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var main = result.Bodies.Single();
        var first = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundAssignmentStatement>(main.Statements[0]).Value);
        var second = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundAssignmentStatement>(main.Statements[1]).Value);
        Assert.Same(Lookup<ClassTypeSymbol>(result, "TObject").Methods.Single(), first.Routine);
        Assert.Same(Lookup<ClassTypeSymbol>(result, "TB").Methods.Single(), second.Routine);
        Assert.Equal(MethodKind.Constructor, ((MethodSymbol)second.Routine).MethodKind);

        // nothing finds it: not a call, not a method pointer, not the member list
        var errors = Errors("""
            type
              TC = class class constructor Create; class destructor Destroy; end;
            class constructor TC.Create; begin end;
            class destructor TC.Destroy; begin end;
            var P: procedure of object;
            begin
              TC.Create;
              P := TC.Destroy;
            end.
            """);
        Assert.Equal(["E2005(7,6): 'TC' does not contain a member named 'Create'", "E2005(8,11): 'TC' does not contain a member named 'Destroy'"], errors);
        var c = Lookup<ClassTypeSymbol>(Bind("type TC = class class constructor Create; end; class constructor TC.Create; begin end; begin end.").Result, "TC");
        Assert.Empty(c.Methods);
        Assert.DoesNotContain(c.Members, m => m.Name == "Create");
    }

    /// <summary>LANG-787: the name is not entered into the member scope, so it never clashes (no E2004).</summary>
    [Fact]
    [Trait("Req", "LANG-787")]
    public void NameDoesNotClashWithMembers()
    {
        Assert.Empty(Errors("""
            type
              TA = class
                F: Integer;
                class constructor Create;
                class destructor F;
                constructor Create;
                destructor Destroy;
              end;
              TR = record
                Create: Integer;
                class constructor Create;
              end;
            class constructor TA.Create; begin end;
            class destructor TA.F; begin end;
            constructor TA.Create; begin end;
            destructor TA.Destroy; begin end;
            class constructor TR.Create; begin end;
            var A: TA;
            begin
              A := TA.Create;
              A.F := 1;
            end.
            """));
    }

    /// <summary>LANG-787: not inherited, no VMT slot; a descendant without one has none, each type runs its own (LANG-789).</summary>
    [Fact]
    [Trait("Req", "LANG-787")]
    public void NotInherited_EachTypeHasItsOwn()
    {
        var (result, _) = Bind("""
            type
              TA = class class constructor Create; class destructor Destroy; end;
              TB = class(TA) end;
              TC = class(TB) class constructor Create; end;
            class constructor TA.Create; begin end;
            class destructor TA.Destroy; begin end;
            class constructor TC.Create; begin end;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var (a, b, c) = (Lookup<ClassTypeSymbol>(result, "TA"), Lookup<ClassTypeSymbol>(result, "TB"), Lookup<ClassTypeSymbol>(result, "TC"));
        Assert.Null(b.ClassConstructor);
        Assert.Null(b.ClassDestructor);
        Assert.NotSame(a.ClassConstructor, c.ClassConstructor);
        Assert.Null(c.ClassDestructor);
        Assert.Equal(-1, a.ClassConstructor!.VmtSlot);
        Assert.Empty(a.VirtualSlots);
        Assert.Equal([a, c], Lowerer.ClassConstructorItems(result.Unit).Select(i => i.Type));
    }

    /// <summary>LANG-787, LANG-648: the RTTI of a class is the same with and without a class constructor and destructor.</summary>
    [Fact]
    [Trait("Req", "LANG-787")]
    [Trait("Req", "LANG-648")]
    public void NotInRttiMethodList()
    {
        const string Class = """
            unit M;
            interface
            type
              {$RTTI BROAD}
              TA = class
              public
                class procedure Run; static;
                procedure Go;
                {0}
              end;
            implementation
            class procedure TA.Run; begin end;
            procedure TA.Go; begin end;
            {1}
            end.
            """;
        var plain = Emit.IrSnapshotTests.Emit(Class.Replace("{0}", "").Replace("{1}", ""));
        var with = Emit.IrSnapshotTests.Emit(Class.Replace("{0}", "class constructor Create; class destructor Destroy;")
            .Replace("{1}", "class constructor TA.Create; begin end; class destructor TA.Destroy; begin end;"));

        static string Rtti(string ir) => string.Join("\n", ir.Split('\n').Where(l => l.Contains("$rtti", StringComparison.Ordinal) && l.StartsWith('@')));
        Assert.Contains("$cctor", with);
        Assert.NotEmpty(Rtti(plain));
        Assert.Equal(Rtti(plain), Rtti(with));
    }

    [Theory]
    [Trait("Req", "LANG-786")]
    [Trait("Req", "LANG-788")]
    [InlineData("type TA = class class constructor Create(X: Integer); end; class constructor TA.Create; begin end; begin end.",
        "E2665(1,41): A class constructor cannot have parameters or type parameters")]
    [InlineData("type TA = class class destructor Destroy<T>; end; class destructor TA.Destroy; begin end; begin end.",
        "E2665(1,41): A class destructor cannot have parameters or type parameters")]
    [InlineData("type TA = class class constructor Create; end; class constructor TA.Create(X: Integer); begin end; begin end.",
        "E2665(1,75): A class constructor cannot have parameters or type parameters")]
    [InlineData("type TA = class class constructor Create(); end; class constructor TA.Create(); begin end; begin end.", null)]
    public void Parameters_E2665(string source, string? expected)
    {
        Assert.Equal(expected is null ? [] : [expected], Errors(source));
    }

    [Fact]
    [Trait("Req", "LANG-786")]
    public void Second_E2666()
    {
        Assert.Equal(["E2666(1,61): Type 'TA' already has a class constructor"],
            Errors("type TA = class class constructor Create; class constructor Init; end; class constructor TA.Create; begin end; begin end."));
        Assert.Equal(["E2666(1,58): Type 'TR' already has a class destructor"],
            Errors("type TR = record class destructor Done; class destructor Done2; end; class destructor TR.Done; begin end; begin end."));
        // one class constructor plus one class destructor is fine
        Assert.Empty(Errors("type TA = class class constructor Create; class destructor Create; end; "
            + "class constructor TA.Create; begin end; class destructor TA.Create; begin end; begin end."));
        // a generic names its type parameter list; like every error of a generic's declaration it is reported per instance (LANG-711)
        Assert.Equal(["E2710(1,66): In instantiation 'TBox<Integer>': Type 'TBox<T>' already has a class constructor",
                      "E2710(1,66): In instantiation 'TBox<Byte>': Type 'TBox<T>' already has a class constructor"],
            Errors("type TBox<T> = class class constructor Create; class constructor Init; end; class constructor TBox<T>.Create; begin end; "
                + "var A: TBox<Integer>; B: TBox<Byte>; begin end."));
    }

    [Theory]
    [Trait("Req", "LANG-786")]
    [InlineData("virtual")]
    [InlineData("override")]
    [InlineData("static")]
    [InlineData("overload")]
    [InlineData("cdecl")]
    [InlineData("inline")]
    [InlineData("safecall")]
    public void Directive_E2667(string directive)
    {
        Assert.Equal([$"E2667(1,43): Directive '{directive}' is not allowed on a class constructor"],
            Errors($"type TA = class class constructor Create; {directive}; end; class constructor TA.Create; begin end; begin end."));
    }

    [Fact]
    [Trait("Req", "LANG-786")]
    public void HintDirectives_Accepted()
    {
        Assert.Empty(Errors("""
            type
              TA = class
                class constructor Create; deprecated 'old';
                class destructor Destroy; platform; experimental;
              end;
            class constructor TA.Create; begin end;
            class destructor TA.Destroy; begin end;
            begin end.
            """));
    }

    [Fact]
    [Trait("Req", "LANG-786")]
    [Trait("Req", "LANG-730")]
    public void RecordClassDestructor_Allowed_PlainDestructor_E2730()
    {
        Assert.Empty(Errors("type TR = record X: Integer; class destructor Done; end; class destructor TR.Done; begin end; begin end."));
        Assert.Equal(["E2730(1,30): 'destructor' is not allowed in a record"],
            Errors("type TR = record X: Integer; destructor Done; end; begin end."));
    }

    [Fact]
    [Trait("Req", "LANG-786")]
    public void InterfaceAndHelper_StayE2900()
    {
        Assert.Equal(["E2900(1,23): Feature not supported: interface constructor"],
            Errors("type IFoo = interface class constructor Create; end; begin end."));
        Assert.Equal(["E2900(1,47): Feature not supported: helper destructor"],
            Errors("type TA = class end; TH = class helper for TA class destructor Destroy; end; begin end."));
    }

    [Theory]
    [Trait("Req", "LANG-788")]
    [InlineData("type TA = class class constructor Create; end; class constructor TA.Init; begin end; begin end.",
        "E2606(1,69): 'Init' is not a member of class 'TA'")]
    [InlineData("type TA = class class constructor Create; end; constructor TA.Create; begin end; begin end.",
        "E2606(1,63): 'Create' is not a member of class 'TA'")]
    [InlineData("type TA = class constructor Create; end; class constructor TA.Create; begin end; begin end.",
        "E2606(1,63): 'Create' is not a member of class 'TA'")]
    [InlineData("type TA = class class destructor Destroy; end; class constructor TA.Destroy; begin end; begin end.",
        "E2606(1,69): 'Destroy' is not a member of class 'TA'")]
    public void ImplementationMismatch_E2606(string source, string expected)
    {
        Assert.Contains(expected, Errors(source));
    }

    [Fact]
    [Trait("Req", "LANG-788")]
    public void MissingImplementation_E2605()
    {
        Assert.Equal(["E2605(1,35): Method 'TA.Create' is not implemented", "E2605(1,60): Method 'TA.Done' is not implemented"],
            Errors("type TA = class class constructor Create; class destructor Done; end; begin end."));
        Assert.Equal(["E2605(1,36): Method 'TR.Create' is not implemented"],
            Errors("type TR = record class constructor Create; end; begin end."));
    }

    /// <summary>LANG-788 b: no <c>Self</c>; class vars, constants, nested types and class static methods by simple name; instance members E2608.</summary>
    [Fact]
    [Trait("Req", "LANG-788")]
    public void Body_StaticScope_InstanceMember_E2608()
    {
        Assert.Empty(Errors("""
            type
              TA = class
              const Limit = 3;
              type TKind = (kOne, kTwo);
              class var Count: Integer;
              class var Kind: TKind;
              class procedure Reset; static;
              class constructor Create;
              class destructor Destroy;
              end;
            class procedure TA.Reset; begin Count := 0 end;
            class constructor TA.Create;
            var I: Integer;
            begin
              Reset;
              for I := 1 to Limit do Inc(Count);
              Kind := kTwo;
            end;
            class destructor TA.Destroy;
            begin
              if Count > 0 then Exit;
              Count := -1;
            end;
            begin end.
            """));
        Assert.Equal(["E2608(3,36): Instance member 'F' is not accessible from a class method",
                      "E2608(4,30): Instance member 'Go' is not accessible from a class method"],
            Errors("""
            type TA = class F: Integer; procedure Go; class constructor Create; end;
            procedure TA.Go; begin end;
            class constructor TA.Create; begin F := 1;
                                         Go end;
            begin end.
            """));
    }

    /// <summary>LANG-788 b, LANG-711: in a generic the type parameters are in scope; the body is bound per instance.</summary>
    [Fact]
    [Trait("Req", "LANG-788")]
    public void Body_GenericTypeParameters()
    {
        var (result, _) = Bind("""
            type
              TBox<T> = class
                class var Size: Integer;
                class var Empty: T;
                class constructor Create;
              end;
            class constructor TBox<T>.Create;
            var Local: T;
            begin
              Size := SizeOf(T);
              Local := Default(T);
              Empty := Local;
            end;
            var A: TBox<Int64>; B: TBox<Byte>;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var a = Assert.IsType<ClassTypeSymbol>(Lookup<VariableSymbol>(result, "A").Type);
        var b = Assert.IsType<ClassTypeSymbol>(Lookup<VariableSymbol>(result, "B").Type);
        Assert.NotSame(a.ClassConstructor, b.ClassConstructor);
        var body = result.Routines.Single(r => ReferenceEquals(r.Routine, a.ClassConstructor));
        Assert.Contains(body.Scope.Symbols.OfType<VariableSymbol>(), v => v.Name == "Local" && v.Type.Name == "Int64");
        Assert.Contains(result.Routines, r => ReferenceEquals(r.Routine, b.ClassConstructor));
    }

    /// <summary>LANG-788 c: bare <c>inherited</c> is E2668; <c>inherited Name</c> works as in a class static method.</summary>
    [Fact]
    [Trait("Req", "LANG-788")]
    public void BareInherited_E2668()
    {
        Assert.Equal(["E2668(4,36): 'inherited' needs a method name in a class constructor",
                      "E2668(5,36): 'inherited' needs a method name in a class destructor"],
            Errors("""
            type
              TA = class class procedure Setup; static; end;
              TB = class(TA) class constructor Create; class destructor Destroy; end;
            class constructor TB.Create; begin inherited end;
            class destructor TB.Destroy; begin inherited; end;
            class procedure TA.Setup; begin end;
            begin end.
            """));
        Assert.Empty(Errors("""
            type
              TA = class class procedure Setup; static; end;
              TB = class(TA) class constructor Create; end;
            class procedure TA.Setup; begin end;
            class constructor TB.Create; begin inherited Setup end;
            begin end.
            """));
    }
}
