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
}
