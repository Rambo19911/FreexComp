using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-126: attribute resolution, class-level attributes and the recognised built-ins (LANG-760…763).</summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-760")]
public sealed class AttributeBindingTests
{
    private const string SystemSource = """
        unit System;
        interface
        type
          TObject = class constructor Create; end;
          TCustomAttribute = class(TObject) end;
        var Input, Output: Text;
        implementation
        constructor TObject.Create; begin end;
        end.
        """;

    private const string Declarations = """
        program P;
        type
          TagAttribute = class(TCustomAttribute)
            constructor Create(N: Integer); overload;
            constructor Create; overload;
          end;
          TPlain = class end;
        constructor TagAttribute.Create(N: Integer); begin end;
        constructor TagAttribute.Create; begin end;

        """;

    private static BindingResult BindWithAttributes(string text)
    {
        var types = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", SystemSource)), types);
        Assert.Empty(system.Diagnostics);
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, text));
        Assert.Empty(tree.Diagnostics);
        return Binder.BindCompilationUnit(tree, types, system);
    }

    [Fact]
    [Trait("Req", "LANG-762")]
    public void ClassAttributes_AreKeptWithAThunkEach()
    {
        var result = BindWithAttributes(Declarations + "type [Tag(1)] [TagAttribute] [Tag] TFoo = class end;\nbegin end.");

        Assert.Empty(Format(result.Diagnostics));
        var foo = Assert.IsType<ClassTypeSymbol>(result.Scope.Lookup("TFoo"));
        Assert.Equal(3, foo.Attributes.Length);
        Assert.All(foo.Attributes, a => Assert.Equal("TagAttribute", a.Type.Name));
        Assert.Equal(["TFoo#attr1", "TFoo#attr2", "TFoo#attr3"], foo.Attributes.Select(a => a.Thunk.Name));
        Assert.All(foo.Attributes, a => Assert.Contains(result.Routines, r => ReferenceEquals(r.Routine, a.Thunk)));
    }

    [Theory]
    [Trait("Req", "LANG-761")]
    [InlineData("type [Tag('x')] TFoo = class end;", "E2123")]                         // no constructor takes a string
    [InlineData("var V: Integer; type [Tag(V)] TFoo = class end;", "E2050")]           // not a constant
    [InlineData("type [TPlain] TFoo = class end;", "W1070")]                           // not an attribute class
    [InlineData("type [Missing] TFoo = class end;", "W1070")]
    [InlineData("type TFoo = class [Weak] F: TObject; end;", "H2010")]
    [InlineData("type TFoo = class [Unsafe] F: TObject; [Volatile] G: Integer; end;", "H2010,H2010")]
    [InlineData("procedure Q([Ref] const X: Integer); begin end;", "")]
    public void Diagnostics(string declaration, string codes) =>
        Assert.Equal(codes, string.Join(",", BindWithAttributes(Declarations + declaration + "\nbegin end.").Diagnostics.Select(d => d.Code)));

    [Fact]
    [Trait("Req", "LANG-762")]
    public void OtherAttributes_AreCheckedThenDiscarded()
    {
        var result = BindWithAttributes(Declarations + "type TFoo = class [Tag(2)] F: Integer; [Tag] procedure M([Tag] X: Integer); end;\n"
            + "procedure TFoo.M(X: Integer); begin F := X end;\nbegin end.");

        Assert.Empty(Format(result.Diagnostics));
        Assert.Empty(Assert.IsType<ClassTypeSymbol>(result.Scope.Lookup("TFoo")).Attributes);
    }
}
