using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-299 (com-interfaces.md §9.1): the layout of the virtual method table follows the declarations in <c>System</c>.</summary>
[Trait("Phase", "8")]
public sealed class VmtLayoutTests
{
    /// <summary>A <c>System</c> whose <c>TObject</c> declares eleven virtual methods (ABI-073 plus one).</summary>
    private const string SystemWithElevenSlots = """
        unit System;
        interface
        type
          TObject = class
            destructor Destroy; virtual;
            procedure AfterConstruction; virtual;
            procedure BeforeDestruction; virtual;
            class function NewInstance: TObject; virtual;
            procedure FreeInstance; virtual;
            function ToString: string; virtual;
            function Equals(Obj: TObject): Boolean; virtual;
            function GetHashCode: Integer; virtual;
            procedure Dispatch(var Message); virtual;
            procedure DefaultHandler(var Message); virtual;
            function Eleventh(Obj: TObject; Addr: Pointer): Integer; virtual;
          end;
        var Input, Output: Text;
        implementation
        destructor TObject.Destroy; begin end;
        procedure TObject.AfterConstruction; begin end;
        procedure TObject.BeforeDestruction; begin end;
        class function TObject.NewInstance: TObject; begin Result := nil; end;
        procedure TObject.FreeInstance; begin end;
        function TObject.ToString: string; begin Result := ''; end;
        function TObject.Equals(Obj: TObject): Boolean; begin Result := False; end;
        function TObject.GetHashCode: Integer; begin Result := 0; end;
        procedure TObject.Dispatch(var Message); begin end;
        procedure TObject.DefaultHandler(var Message); begin end;
        function TObject.Eleventh(Obj: TObject; Addr: Pointer): Integer; begin Result := 0; end;
        end.
        """;

    [Fact]
    [Trait("Req", "ABI-073")]
    public void TObjectSlotCountFollowsSystemDeclaration()
    {
        var types = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", SystemWithElevenSlots)), types);
        Assert.Empty(Format(system.Diagnostics));
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, """
            type
              TFirst = class
                procedure Own; virtual;
                function Eleventh(Obj: TObject; Addr: Pointer): Integer; override;
                procedure Second; virtual;
              end;
            procedure TFirst.Own; begin end;
            function TFirst.Eleventh(Obj: TObject; Addr: Pointer): Integer; begin Result := 1; end;
            procedure TFirst.Second; begin end;
            begin end.
            """));
        Assert.Empty(tree.Diagnostics);

        var result = Binder.BindCompilationUnit(tree, types, system);

        Assert.Empty(Format(result.Diagnostics));
        var root = Assert.IsType<ClassTypeSymbol>(system.Declarations.Scope.Lookup("TObject"));
        var first = Assert.IsType<ClassTypeSymbol>(result.Declarations.Scope.Lookup("TFirst"));
        Assert.Equal(11, root.VmtSlotCount);
        Assert.Equal("Eleventh", root.VirtualSlots[10].Name);
        Assert.Same(root, first.Parent);
        Assert.Equal([11, 10, 12], first.Methods.Select(m => m.VmtSlot));          // the first slot of a descendant follows TObject's last
        Assert.Equal(13, first.VmtSlotCount);
        Assert.Same(first.Methods[1], first.VirtualSlots[10]);
    }
}
