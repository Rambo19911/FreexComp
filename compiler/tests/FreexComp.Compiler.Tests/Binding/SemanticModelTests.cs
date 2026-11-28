// The model's lookups are synchronous index reads over an already bound compilation; the optional token is left out on purpose.
#pragma warning disable xUnit1051

using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>api.md §6 (CMP-230…232): the public semantic model over a compilation (C-129).</summary>
[Trait("Phase", "3")]
public sealed class SemanticModelTests
{
    private const string Dir = @"C:\p";
    private static readonly string MainPath = Path.Combine(Dir, "P.pas");

    private static SemanticModel Model(string program, params (string Name, string Source)[] units) => Model(program, MainPath, units);

    private static SemanticModel Model(string program, string path, params (string Name, string Source)[] units)
    {
        var fs = new InMemoryFileSystem().Add(MainPath, program).AddSystem();
        foreach (var (name, source) in units)
        {
            fs.Add(Path.Combine(Dir, name), source);
        }

        var options = new CompilationOptions { MainFile = MainPath, RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        return Compilation.Create(options, [], fs).GetSemanticModel(path);
    }

    /// <summary>The innermost node of type <typeparamref name="T"/> starting at the <paramref name="occurrence"/>-th <paramref name="marker"/>.</summary>
    private static T Node<T>(SemanticModel model, string marker, int occurrence = 0)
        where T : SyntaxNode
    {
        var text = model.SyntaxTree.Text.Text;
        var start = -1;
        for (var i = 0; i <= occurrence; i++)
        {
            start = text.IndexOf(marker, start + 1, StringComparison.Ordinal);
            Assert.True(start >= 0, marker);
        }

        return model.SyntaxTree.Root.DescendantNodes().OfType<T>().Where(n => n.Span.Start == start).OrderBy(n => n.Span.Length).First();
    }

    private static int Position(SemanticModel model, string marker) => model.SyntaxTree.Text.Text.IndexOf(marker, StringComparison.Ordinal);

    private const string Program = """
        program P;
        type
          TColor = (Red, Green);
          TRec = record F: Integer; end;
          TFoo = class
          private
            FSecret: Integer;
          public
            Count: Integer;
            procedure Bump;
          end;
          TBar = class(TFoo)
          end;
        const
          N = 3;
        var
          X: Integer;
          Foo: TFoo;
        procedure F(A: Integer); overload; begin end;
        procedure F(const S: string); overload; begin end;
        procedure TFoo.Bump;
        begin
          Inc(Count);
        end;
        function Twice(V: Integer): Integer;
        var X: Integer;
        begin
          X := V * 2;
          Result := X;
        end;
        var R: TRec; C: TColor;
        begin
          X := N * 2;
          R.F := X;
          C := Green;
          F('a');
          F(1);
          Foo := TFoo.Create;
          Foo.Count := Twice(X);
        end.
        """;

    [Fact]
    [Trait("Req", "CMP-230")]
    public void GetSymbolInfo_Identifier_BindsToTheVariable()
    {
        var model = Model(Program);

        var info = model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "X := N"));

        var variable = Assert.IsType<VariableSymbol>(info.Symbol);
        Assert.Equal("X", variable.Name);
        Assert.True(variable.IsGlobal);
        Assert.Equal(CandidateReason.None, info.Reason);
    }

    [Fact]
    [Trait("Req", "CMP-230")]
    public void GetSymbolInfo_LocalShadowsGlobal()
    {
        var model = Model(Program);

        var local = Assert.IsType<VariableSymbol>(model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "X := V")).Symbol);

        Assert.False(local.IsGlobal);
        Assert.Equal("Twice", local.ContainingSymbol?.Name);
    }

    [Fact]
    [Trait("Req", "CMP-230")]
    public void GetSymbolInfo_MemberAccess_IsTheMember()
    {
        var model = Model(Program);

        var access = Node<MemberAccessExpressionSyntax>(model, "R.F");
        Assert.Equal("F", Assert.IsType<FieldSymbol>(model.GetSymbolInfo(access).Symbol).Name);
        Assert.Equal("F", Assert.IsType<FieldSymbol>(model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "F :=", 0)).Symbol).Name);
        Assert.Equal("R", model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "R.F")).Symbol?.Name);
        Assert.Equal("Count", Assert.IsType<FieldSymbol>(model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "Count :=")).Symbol).Name);
    }

    [Fact]
    [Trait("Req", "CMP-230")]
    public void GetSymbolInfo_Invocation_IsTheChosenOverload()
    {
        var model = Model(Program);

        var call = model.GetSymbolInfo(Node<InvocationExpressionSyntax>(model, "F('a')"));
        var routine = Assert.IsAssignableFrom<RoutineSymbol>(call.Symbol);
        Assert.Equal(ParameterMode.Const, routine.Parameters[0].Mode);

        var byName = Assert.IsAssignableFrom<RoutineSymbol>(model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "F(1)")).Symbol);
        Assert.Equal(ParameterMode.Value, byName.Parameters[0].Mode);
        Assert.Equal("Twice", model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "Twice(X)")).Symbol?.Name);
        Assert.Equal("Create", model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "Create")).Symbol?.Name);
    }

    [Fact]
    [Trait("Req", "CMP-230")]
    public void GetSymbolInfo_TypesEnumMembersConstantsAndUnits()
    {
        var model = Model("program P;\nuses U1;\nvar X: TThing; C: TColor;\nbegin C := Blue; X := nil; C := TColor.Red end.",
            ("U1.pas", "unit U1;\ninterface\ntype TThing = class end; TColor = (Red, Blue);\nimplementation\nend."));

        Assert.Equal("TThing", Assert.IsAssignableFrom<TypeSymbol>(model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "TThing")).Symbol).Name);
        Assert.Equal("Blue", Assert.IsType<EnumMemberSymbol>(model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "Blue")).Symbol).Name);
        Assert.Equal("Red", Assert.IsType<EnumMemberSymbol>(model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "Red")).Symbol).Name);
        Assert.Equal("TColor", model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "TColor.Red")).Symbol?.Name);
        Assert.Equal("U1", Assert.IsType<UnitSymbol>(model.GetSymbolInfo(Node<UsedUnitSyntax>(model, "U1")).Symbol).Name);
    }

    [Fact]
    [Trait("Req", "CMP-230")]
    public void GetSymbolInfo_Unresolved_IsNotFound()
    {
        var model = Model("program P;\nbegin Nowhere := 1 end.");

        var info = model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "Nowhere"));

        Assert.Null(info.Symbol);
        Assert.Equal(CandidateReason.NotFound, info.Reason);
        Assert.Contains(model.GetDiagnostics(), d => d.Code == "E2003");
    }

    [Fact]
    [Trait("Req", "CMP-230")]
    public void GetTypeInfo_AndConstantValue()
    {
        var model = Model(Program);
        var product = Node<BinaryExpressionSyntax>(model, "N * 2");

        Assert.Equal(TypeKind.Integer, model.GetTypeInfo(product).Type?.TypeKind);
        Assert.Equal(new System.Numerics.BigInteger(6), model.GetConstantValue(product)?.Value);
        Assert.Equal("TFoo", model.GetTypeInfo(Node<IdentifierNameSyntax>(model, "Foo.Count")).Type?.Name);
        Assert.Null(model.GetConstantValue(Node<IdentifierNameSyntax>(model, "X := N")));
    }

    [Fact]
    [Trait("Req", "CMP-230")]
    public void GetDeclaredSymbol()
    {
        var model = Model(Program);

        Assert.Equal("TFoo", Assert.IsType<ClassTypeSymbol>(model.GetDeclaredSymbol(Node<TypeDeclarationSyntax>(model, "TFoo = class"))).Name);
        Assert.Equal("Twice", Assert.IsAssignableFrom<RoutineSymbol>(model.GetDeclaredSymbol(Node<RoutineDeclarationSyntax>(model, "function Twice"))).Name);
        Assert.Equal("Bump", Assert.IsType<MethodSymbol>(model.GetDeclaredSymbol(Node<RoutineDeclarationSyntax>(model, "procedure TFoo.Bump"))).Name);
        Assert.Equal("X", Assert.IsType<VariableSymbol>(model.GetDeclaredSymbol(Node<VarDeclarationSyntax>(model, "X: Integer;\n  Foo"))).Name);
        Assert.Equal("V", Assert.IsType<ParameterSymbol>(model.GetDeclaredSymbol(Node<ParameterSyntax>(model, "V: Integer"))).Name);
        Assert.Equal("Count", Assert.IsType<FieldSymbol>(model.GetDeclaredSymbol(Node<FieldDeclarationSyntax>(model, "Count: Integer"))).Name);
        Assert.Equal("Bump", Assert.IsType<MethodSymbol>(model.GetDeclaredSymbol(Node<MethodDeclarationSyntax>(model, "procedure Bump"))).Name);
        Assert.Equal("Green", Assert.IsType<EnumMemberSymbol>(model.GetDeclaredSymbol(Node<EnumMemberSyntax>(model, "Green"))).Name);
        Assert.Equal("N", Assert.IsType<ConstantSymbol>(model.GetDeclaredSymbol(Node<ConstDeclarationSyntax>(model, "N = 3"))).Name);
        Assert.Same(model.Unit, model.GetDeclaredSymbol(model.SyntaxTree.Root));
        Assert.Null(model.GetDeclaredSymbol(Node<BinaryExpressionSyntax>(model, "N * 2")));

        var token = Node<IdentifierNameSyntax>(model, "Foo: TFoo").Identifier;
        Assert.Equal("Foo", model.GetDeclaredSymbol(token)?.Name);
    }

    [Fact]
    [Trait("Req", "CMP-232")]
    public void LookupSymbols_InnermostFirstWithoutShadowedNames()
    {
        var model = Model(Program);
        var position = Position(model, "X := V");

        var visible = model.LookupSymbols(position);

        var x = Assert.Single(visible, s => s.Name == "X");
        Assert.False(Assert.IsType<VariableSymbol>(x).IsGlobal);
        Assert.Contains(visible, s => s.Name == "V");
        Assert.Contains(visible, s => s.Name == "TFoo");
        Assert.Contains(visible, s => s.Name == "Twice");
        Assert.Contains(visible, s => s.Name == "Writeln");                   // built-ins and System are visible
        Assert.True(visible.ToList().FindIndex(s => s.Name == "V") < visible.ToList().FindIndex(s => s.Name == "TFoo"));
        Assert.Equal(2, visible.Count(s => s.Name == "F"));                      // an overload set
        Assert.Equal("X", Assert.Single(model.LookupSymbols(position, "x")).Name);
    }

    [Fact]
    [Trait("Req", "CMP-232")]
    public void LookupSymbols_ContainerMembers_IncludeInheritedAndRespectVisibility()
    {
        var model = Model(Program, ("U1.pas", "unit U1; interface implementation end."));
        var bar = (TypeSymbol)model.GetDeclaredSymbol(Node<TypeDeclarationSyntax>(model, "TBar"))!;

        var members = model.LookupSymbols(Position(model, "Foo := TFoo"), container: bar);

        Assert.Contains(members, s => s.Name == "Count");
        Assert.Contains(members, s => s.Name == "Bump");
        Assert.Contains(members, s => s.Name == "Create");                       // from TObject
        Assert.Contains(members, s => s.Name == "FSecret");                      // private: same unit
    }

    [Fact]
    [Trait("Req", "CMP-232")]
    public void IsAccessible_PrivateMemberOfAnotherUnit_IsNot()
    {
        var model = Model("program P;\nuses U1;\nvar T: TThing;\nbegin end.",
            ("U1.pas", "unit U1;\ninterface\ntype TThing = class private FHidden: Integer; public Shown: Integer; end;\nimplementation\nend."));
        var thing = (TypeSymbol)model.GetSymbolInfo(Node<IdentifierNameSyntax>(model, "TThing")).Symbol!;
        var position = Position(model, "begin");

        var members = model.LookupSymbols(position, container: thing);

        Assert.Contains(members, s => s.Name == "Shown");
        Assert.DoesNotContain(members, s => s.Name == "FHidden");
        Assert.False(model.IsAccessible(position, thing.GetMembers("FHidden").Single()));
    }

    [Fact]
    [Trait("Req", "CMP-230")]
    public void GetEnclosingSymbol()
    {
        var model = Model(Program);

        Assert.Equal("Twice", model.GetEnclosingSymbol(Position(model, "X := V"))?.Name);
        Assert.Equal("Bump", model.GetEnclosingSymbol(Position(model, "Inc(Count)"))?.Name);
        Assert.Equal("TFoo", model.GetEnclosingSymbol(Position(model, "FSecret"))?.Name);
        Assert.Same(model.Unit, model.GetEnclosingSymbol(Position(model, "C := Green")));
    }

    [Fact]
    [Trait("Req", "CMP-230")]
    public void GetOverloadCandidates()
    {
        var model = Model(Program);

        var candidates = model.GetOverloadCandidates(Node<InvocationExpressionSyntax>(model, "F(1)"));

        Assert.Equal(2, candidates.Length);
        Assert.All(candidates, c => Assert.Equal("F", c.Name));
        Assert.Single(model.GetOverloadCandidates(Node<InvocationExpressionSyntax>(model, "Twice(X)")));
    }

    [Fact]
    [Trait("Req", "CMP-201")]
    public void FileNotReachableFromTheMainFile_IsAnalysedAsARoot()
    {
        var model = Model("program P; begin end.", Path.Combine(Dir, "Lone.pas"), ("Lone.pas", "unit Lone;\ninterface\nconst K = 1;\nimplementation\nend."));

        Assert.Equal("Lone", model.Unit.Name);
        Assert.Equal("K", Assert.IsType<ConstantSymbol>(model.GetDeclaredSymbol(Node<ConstDeclarationSyntax>(model, "K = 1"))).Name);
    }

    /// <summary>
    /// C-407: a call on a class name has its own type — the class for a constructor (<c>TFoo.Create</c>, <c>TFoo.Make(1)</c>), the
    /// result for a class function — not the class reference of its receiver, which belongs to the qualifier <c>TFoo</c>.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-230")]
    public void ConstructorCallTypeIsTheClass()
    {
        var model = Model("""
            program P;
            type
              TFoo = class
                constructor Make(N: Integer);
                class function Count: Integer;
              end;
            constructor TFoo.Make(N: Integer); begin end;
            class function TFoo.Count: Integer; begin Result := 0; end;
            var Foo: TFoo; N: Integer;
            begin
              Foo := TFoo.Create;
              Foo := TFoo.Make(1);
              N := TFoo.Count;
            end.
            """);
        Assert.DoesNotContain(model.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

        var create = Node<MemberAccessExpressionSyntax>(model, "TFoo.Create");
        var info = model.GetTypeInfo(create);
        Assert.Equal("TFoo", Assert.IsType<ClassTypeSymbol>(info.Type).Name);
        Assert.Same(info.Type, info.ConvertedType);
        Assert.Equal(MethodKind.Constructor, Assert.IsType<MethodSymbol>(model.GetSymbolInfo(create).Symbol).MethodKind);

        var make = Node<InvocationExpressionSyntax>(model, "TFoo.Make(1)");
        Assert.Same(info.Type, model.GetTypeInfo(make).Type);
        Assert.Equal("Make", model.GetSymbolInfo(make).Symbol?.Name);

        Assert.Equal("Integer", model.GetTypeInfo(Node<MemberAccessExpressionSyntax>(model, "TFoo.Count", occurrence: 1)).Type?.Name);   // 0: the heading

        // the qualifier names the class; as an expression it is the class reference the call is made on
        foreach (var (marker, occurrence) in new[] { ("TFoo.Create", 0), ("TFoo.Make(1)", 0), ("TFoo.Count", 1) })
        {
            var qualifier = Node<IdentifierNameSyntax>(model, marker, occurrence);
            Assert.Same(info.Type, model.GetSymbolInfo(qualifier).Symbol);
            Assert.Same(info.Type, Assert.IsType<ClassRefTypeSymbol>(model.GetTypeInfo(qualifier).Type).Class);
        }
    }

    [Fact]
    public void BrokenCode_NeverThrows()
    {
        var model = Model("program P;\nprocedure Q(; begin X := ; end;\nbegin Q( end");

        foreach (var node in model.SyntaxTree.Root.DescendantNodes())
        {
            _ = model.GetSymbolInfo(node);
            _ = model.GetTypeInfo(node);
            _ = model.GetDeclaredSymbol(node);
            _ = model.GetEnclosingSymbol(node.Span.Start);
        }

        Assert.NotEmpty(model.LookupSymbols(0));
    }

    [Fact]
    public void GetDirectiveState()
    {
        var model = Model("program P;\n{$R+}\nvar X: Integer;\nbegin end.");

        Assert.False(model.GetDirectiveState(0).RangeChecks);
        Assert.True(model.GetDirectiveState(Position(model, "var")).RangeChecks);
    }

    /// <summary>
    /// CMP-1473 (C-415) on CMP-230…232: inside an anonymous method the visible symbols are its own parameters, locals and
    /// <c>Result</c> before those of the body it is written in (also when an inline variable of that body is in scope there);
    /// the enclosing symbol stays the routine it is written in; and the callee name of a call through a value — a method
    /// reference, a procedural variable — is that value, not the signature of its type.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1473")]
    public void AnonymousMethod_ScopeEnclosingSymbolAndCallThroughAValue()
    {
        var model = Model("""
            program P;
            type
              TIntFunc = reference to function(Delta: Integer): Integer;
              TPlain = function(N: Integer): Integer;
            function Twice(N: Integer): Integer;
            begin
              Result := N * 2;
            end;
            function Work(Limit: Integer): TIntFunc;
            var
              Count: Integer;
              Plain: TPlain;
            begin
              Count := 0;
              Plain := Twice;
              var Before := Plain(1);
              Result := function(Count: Integer): Integer
                var Mine: Integer;
                begin
                  Mine := Count + Before + Limit;
                  Result := Plain(Mine);
                end;
              var After := Result(2);
              Count := After;
            end;
            begin
            end.
            """);
        var inside = Position(model, "Mine := Count");

        var visible = model.LookupSymbols(inside);
        var count = Assert.Single(visible, s => s.Name == "Count");
        Assert.IsType<ParameterSymbol>(count);                                     // the anonymous function's parameter hides the local
        Assert.Equal("Integer", Assert.IsType<VariableSymbol>(Assert.Single(visible, s => s.Name == "Result")).Type.Name);
        Assert.Contains(visible, s => s.Name == "Mine");
        Assert.Contains(visible, s => s.Name == "Before");
        Assert.Contains(visible, s => s.Name == "Limit");
        Assert.DoesNotContain(visible, s => s.Name == "After");
        Assert.True(visible.IndexOf(count) < visible.IndexOf(visible.Single(s => s.Name == "Limit")));      // innermost scope first

        Assert.Equal("Work", model.GetEnclosingSymbol(inside)?.Name);
        var outside = model.LookupSymbols(Position(model, "Count := After"));
        Assert.IsType<VariableSymbol>(Assert.Single(outside, s => s.Name == "Count"));
        Assert.Equal("TIntFunc", Assert.IsType<VariableSymbol>(Assert.Single(outside, s => s.Name == "Result")).Type.Name);
        Assert.DoesNotContain(outside, s => s.Name == "Mine");

        // a call through a value names the value
        Assert.Equal("var Plain: TPlain", model.GetSymbolInfo(Node<InvocationExpressionSyntax>(model, "Plain(Mine)")).Symbol?.ToDisplayString());
        Assert.Equal("var Plain: TPlain", model.GetSymbolInfo(Node<InvocationExpressionSyntax>(model, "Plain(1)")).Symbol?.ToDisplayString());
        Assert.Equal("var Result: TIntFunc", model.GetSymbolInfo(Node<InvocationExpressionSyntax>(model, "Result(2)")).Symbol?.ToDisplayString());
        Assert.Equal("Integer", model.GetTypeInfo(Node<InvocationExpressionSyntax>(model, "Plain(Mine)")).Type?.Name);   // the call's value
    }
    private const string GenericMethodsUnit = """
        unit UGen;
        interface
        type
          PNode = ^TNode;
          TNode = record
            Next: PNode;
            function Same<T>(Item: T): T; overload;
            function Same(Item: Integer; Extra: Integer): Integer; overload;
            class function Make<T>(const Seed: T): T; static;
          end;
          TUtil = class
            class function Max<T>(A, B: T): T; overload; static;
            class function Max(A: Integer): Integer; overload; static;
            class function Pick<T>(Left, Right: T): T;
            function Keep<T>(var Value: T): T;
          end;
        implementation
        function TNode.Same<T>(Item: T): T; begin Result := Item end;
        function TNode.Same(Item: Integer; Extra: Integer): Integer; begin Result := Item + Extra end;
        class function TNode.Make<T>(const Seed: T): T; begin Result := Seed end;
        class function TUtil.Max<T>(A, B: T): T; begin Result := A end;
        class function TUtil.Max(A: Integer): Integer; begin Result := A end;
        class function TUtil.Pick<T>(Left, Right: T): T; begin Result := Right end;
        function TUtil.Keep<T>(var Value: T): T; begin Result := Value end;
        end.
        """;

    private const string GenericMethodsProgram = """
        program P;
        uses UGen;
        var I: Integer; N: TNode; U: TUtil;
        begin
          I := TUtil.Max<Integer>(1, 2);
          I := TUtil.Max(1);
          I := TUtil.Max(1, 2);
          I := TUtil.Pick(3, 4);
          I := N.Same(5);
          I := N.Same(5, 6);
          I := TNode.Make(7);
          U := TUtil.Create;
          I := U.Keep(I);
        end.
        """;

    /// <summary>
    /// C-455: a parameter in the declaration of a generic method (in a class or a record; instance, class and static methods) is
    /// declared by that method's open instance, whose body the unit's model binds. The open instance may be made while the
    /// program is bound (LANG-979: a call that infers the type arguments) before the declaring unit's implementation part is —
    /// with one thread always (load order), with several when the program's thread is first — and must get its definition all the
    /// same: every model of the same text, in every binding order and in a fork, gives the same symbol at such a parameter.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-230")]
    public void GenericMethodDeclarationParameters_BindTheSameInAFork(int jobs)
    {
        var unitPath = Path.Combine(Dir, "UGen.pas");
        Compilation Create()
        {
            var fs = new InMemoryFileSystem().Add(MainPath, GenericMethodsProgram).Add(unitPath, GenericMethodsUnit).AddSystem();
            return Compilation.Create(new CompilationOptions { MainFile = MainPath, RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj", MaxParallelism = jobs }, [], fs);
        }

        // what every parameter name of the generic methods' declarations (the headings in the types) declares
        List<string> Parameters(Compilation compilation, bool programFirst)
        {
            if (programFirst)
            {
                Assert.DoesNotContain(compilation.GetSemanticModel(MainPath).GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
            }

            var model = compilation.GetSemanticModel(unitPath);
            var lines = new List<string>();
            foreach (var method in model.SyntaxTree.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.Heading.TypeParameterList is not null))
            {
                foreach (var token in method.Heading.ParameterList!.Parameters.SelectMany(p => p.Identifiers).Select(i => i.Identifier))
                {
                    var parameter = Assert.IsType<ParameterSymbol>(model.GetDeclaredSymbol(token));
                    var owner = Assert.IsType<MethodSymbol>(parameter.ContainingSymbol);
                    Assert.Equal(token.Text, parameter.Name);
                    Assert.NotNull(owner.Definition);                           // the open instance, with its implementation
                    Assert.NotNull(owner.GenericMethodDefinition);
                    lines.Add($"{token.Span.Start} {parameter.ToDisplayString()} of {owner.ToDisplayString()} at {string.Join(",", parameter.Locations.Select(l => l.Span))}");
                }
            }

            Assert.Equal(7, lines.Count);
            return lines;
        }

        var expected = Parameters(Create(), programFirst: false);
        Assert.Equal(expected, Parameters(Create(), programFirst: true));

        // a fork of the same length (the interface part changed: both files are bound again), from a bound and from an unbound snapshot
        var changed = SourceText.From(unitPath, GenericMethodsUnit.Replace("PNode", "PLink"));
        foreach (var bound in new[] { true, false })
        {
            foreach (var programFirst in new[] { false, true })
            {
                var snapshot = Create();
                if (bound)
                {
                    _ = snapshot.GetSemanticModel(unitPath);
                }

                Assert.Equal(expected, Parameters(snapshot.WithSourceText(changed), programFirst));
            }
        }

        // a fork that changes both files (a method renamed: Max -> Mix), bound in both orders
        var renamedUnit = SourceText.From(unitPath, GenericMethodsUnit.Replace("Max", "Mix"));
        var renamedProgram = SourceText.From(MainPath, GenericMethodsProgram.Replace("Max", "Mix"));
        var renamed = expected.Select(l => l.Replace("Max", "Mix")).ToList();
        foreach (var programFirst in new[] { false, true })
        {
            var snapshot = Create();
            _ = snapshot.GetSemanticModel(unitPath);
            Assert.Equal(renamed, Parameters(snapshot.WithSourceText(renamedUnit).WithSourceText(renamedProgram), programFirst));
        }
    }
}
