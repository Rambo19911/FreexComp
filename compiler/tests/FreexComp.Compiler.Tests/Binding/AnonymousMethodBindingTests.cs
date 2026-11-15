using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-120 / C-121: method reference types, anonymous methods, closures and forwarders (LANG-720…725, CMP-380).</summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-720")]
public sealed class AnonymousMethodBindingTests
{
    private const string SystemSource = """
        unit System;
        interface
        type
          TObject = class constructor Create; end;
          IInterface = interface end;
          TInterfacedObject = class(TObject, IInterface) end;
        var Input, Output: Text;
        implementation
        constructor TObject.Create; begin end;
        end.
        """;

    private static BindingResult BindWithClosures(string text)
    {
        var types = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", SystemSource)), types);
        Assert.Empty(system.Diagnostics);
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, text));
        Assert.Empty(tree.Diagnostics);
        return Binder.BindCompilationUnit(tree, types, system);
    }

    private static string[] Codes(BindingResult result) => [.. result.Diagnostics.Select(d => d.Code)];

    [Fact]
    public void ReferenceType_IsAnInterfaceWithInvoke()
    {
        var result = BindWithClosures("program P; type TF = reference to function(X: Integer): string; begin end.");

        Assert.Empty(Format(result.Diagnostics));
        var type = Assert.IsType<InterfaceTypeSymbol>(result.Scope.Lookup("TF"));
        Assert.True(type.IsMethodReference);
        Assert.Same(result.Types.IInterface, type.Parent);
        Assert.Equal("Invoke", type.Invoke.Name);
        Assert.Equal(0, type.Invoke.VmtSlot);                                     // the stub IInterface has no methods
        Assert.Same(result.Types.String, type.Invoke.ReturnType);
        Assert.True(type.IsManaged);
    }

    [Fact]
    [Trait("Req", "LANG-721")]
    public void CapturedParameter_BecomesAClosureField()
    {
        var result = BindWithClosures("""
            program P;
            type TF = reference to function(X: Integer): Integer;
            function MakeAdder(N: Integer): TF;
            var Unused: Integer;
            begin
              Unused := 0;
              Result := function(X: Integer): Integer begin Result := X + N end;
              if Unused = 0 then Exit;
            end;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var maker = (RoutineSymbol)result.Scope.Lookup("MakeAdder")!;
        var closure = Assert.IsType<ClosureInfo>(maker.Closure);
        Assert.Same(result.Types.TInterfacedObject, closure.Class.Parent);
        Assert.Equal(["N"], closure.Fields.Keys.Select(k => k.Name));
        var implemented = Assert.Single(closure.Class.Interfaces);
        Assert.True(implemented.Interface.IsAnonymousMethod);
        Assert.Contains(result.Routines, r => r.Routine is MethodSymbol { IsAnonymous: true });
        Assert.Contains(closure.Class, ClosureInfo.Of(result.Unit));
    }

    [Theory]
    [Trait("Req", "LANG-723")]
    [InlineData("type TP = procedure; var V: TP; begin V := procedure begin end end.", "E2010")]                   // not a plain procedural type
    [InlineData("type TF = reference to function: Integer; var V: TF; begin V := function: string begin Result := '' end end.", "E2010")]
    public void Incompatible_AnonymousMethods_AreE2010(string text, string code) =>
        Assert.Equal([code], Codes(BindWithClosures("program P; " + text)));

    [Fact]
    [Trait("Req", "LANG-725")]
    public void NestedRoutine_AsReference_IsE2130()
    {
        var result = BindWithClosures("""
            program P;
            type TR = reference to procedure;
            procedure Outer;
            var V: TR;
              procedure Inner; begin end;
            begin V := Inner; V end;
            begin end.
            """);

        Assert.Equal(["E2130"], Codes(result));
    }

    [Fact]
    [Trait("Req", "LANG-485")]
    public void GlobalRoutine_AsReference_UsesAForwarder()
    {
        var result = BindWithClosures("""
            program P;
            type TF = reference to function(X: Integer): Integer;
            function Twice(X: Integer): Integer; begin Result := X * 2 end;
            var F: TF;
            begin F := Twice; F(1) end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        Assert.Single(result.Unit.Forwarders);
        Assert.Contains(result.Routines, r => r.Routine.Name == "#forward1");
    }

    [Fact]
    [Trait("Req", "LANG-721")]
    public void CapturedForVariable_IsReadOnly()
    {
        var result = BindWithClosures("""
            program P;
            type TR = reference to procedure;
            procedure Run;
            var I: Integer; V: TR;
            begin
              for I := 1 to 2 do V := procedure begin I := 5 end;
              V;
            end;
            begin end.
            """);

        Assert.Equal(["E2053"], Codes(result));
    }

    /// <summary>LANG-422 (C-120, for Generics.Collections): an interface parameter may default to nil.</summary>
    [Fact]
    [Trait("Req", "LANG-422")]
    public void InterfaceParameter_DefaultsToNil() =>
        Assert.Empty(Format(BindWithClosures("program P; procedure Q(const I: IInterface = nil); begin end; begin Q end.").Diagnostics));

    [Fact]
    public void CapturedResult_IsUnsupported()
    {
        var result = BindWithClosures("""
            program P;
            type TR = reference to procedure;
            function F: Integer;
            var V: TR;
            begin
              V := procedure begin Result := 1 end;
              V;
            end;
            begin end.
            """);

        Assert.Equal(["E2900"], Codes(result));
    }
}
