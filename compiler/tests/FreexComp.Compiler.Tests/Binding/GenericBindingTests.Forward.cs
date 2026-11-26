using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-346: forward declarations of generic classes and interfaces, instance names as type operands, invocations of a type
/// (generics.md LANG-782…785).
/// </summary>
public sealed partial class GenericBindingTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-782")]
    [Trait("Req", "LANG-783")]
    public void ForwardGeneric_CompletedAndUsedMutually()
    {
        var (result, _) = Bind("""
            program P;
            type
              TNode<T> = class;
              IVisitor<T> = interface;
              TList<T> = class
                Head: TNode<T>;
                Visitor: IVisitor<T>;
              end;
              THolder = class
                Early: TNode<Byte>;
                function Make: TNode<Byte>;
              end;
              TNode<T> = class
                Value: T;
                Next: TNode<T>;
                Owner: TList<T>;
              end;
              IVisitor<T> = interface
                procedure Visit(const Item: T);
              end;
            function THolder.Make: TNode<Byte>;
            begin
              Result := Early;
            end;
            var
              L: TList<Integer>;
              N: TNode<Integer>;
              H: THolder;
            begin
              N := L.Head;
              N.Value := 1;
              L.Visitor.Visit(N.Value);
              H.Early.Value := 2;
            end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var list = Assert.IsType<ClassTypeSymbol>(VariableType(result, "L"));
        var node = Assert.IsType<ClassTypeSymbol>(VariableType(result, "N"));
        Assert.Same(node, list.Fields.Single(f => f.Name == "Head").Type);
        Assert.False(node.IsForward);
        Assert.Same(list, node.Fields.Single(f => f.Name == "Owner").Type);
        Assert.Same(node, node.Fields.Single(f => f.Name == "Next").Type);
        var visitor = Assert.IsType<InterfaceTypeSymbol>(list.Fields.Single(f => f.Name == "Visitor").Type);
        Assert.False(visitor.IsForward);
        Assert.Single(visitor.Methods);

        // an instance named while the generic was still a forward declaration is completed with it (LANG-783)
        var early = Assert.IsType<ClassTypeSymbol>(((ClassTypeSymbol)VariableType(result, "H")).Fields.Single().Type);
        Assert.False(early.IsForward);
        Assert.Same(result.Types.Byte, early.Fields.Single(f => f.Name == "Value").Type);
        Assert.Equal("TNode<Byte>", early.Name);
    }

    /// <summary>LANG-782: names, their order and each parameter's constraint set must equal the forward's (constraint order is free).</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-782")]
    [InlineData("TA<T> = class; TA<U> = class end;", "E2706(1,34): Type parameters of 'TA<T>' differ from its forward declaration")]
    [InlineData("TA<K, V> = class; TA<V, K> = class end;", "E2706(1,37): Type parameters of 'TA<K, V>' differ from its forward declaration")]
    [InlineData("TA<T: class> = class; TA<T> = class end;", "E2706(1,41): Type parameters of 'TA<T>' differ from its forward declaration")]
    [InlineData("TA<T> = class; TA<T: constructor> = class end;", "E2706(1,34): Type parameters of 'TA<T>' differ from its forward declaration")]
    [InlineData("IA<T> = interface; IA<T: record> = interface end;", "E2706(1,38): Type parameters of 'IA<T>' differ from its forward declaration")]
    [InlineData("TA<T: class, constructor> = class; TA<T: constructor, class> = class end;", "")]
    [InlineData("TA<K, V> = class; TA<K; V> = class end;", "")]
    [InlineData("TA<t> = class; TA<T> = class end;", "")]
    public void ForwardGeneric_ParameterListMismatch_E2706(string declarations, string expected) =>
        Assert.Equal(expected.Length == 0 ? [] : [expected], BindDiagnostics("program P; type " + declarations + " begin end."));

    /// <summary>LANG-783: between the forward and its completion an instance is incomplete — no ancestor, parent or member access.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-783")]
    [InlineData("TA<T> = class; TB = class(TA<Integer>) end; TA<T> = class end;", "E2007(1,64): Type 'TA<Integer>' is not yet completely defined")]
    [InlineData("IA<T> = interface; IB = interface(IA<Integer>) end; IA<T> = interface end;", "E2007(1,72): Type 'IA<Integer>' is not yet completely defined")]
    [InlineData("IA<T> = interface; TB = class(TObject, IA<Integer>) end; IA<T> = interface end;", "E2007(1,77): Type 'IA<Integer>' is not yet completely defined")]
    [InlineData("TA<T> = class; TB = class(TA<Integer>) end; TA<T> = class(TB) end;", "E2007(1,64): Type 'TA<Integer>' is not yet completely defined")]
    public void ForwardGeneric_NeedsCompleteType_E2007(string declarations, string expected) =>
        Assert.Equal([expected], BindDiagnostics("program P; type TObject = class end; " + declarations + " begin end."));

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-782")]
    [InlineData("type TA<T> = class; begin end.", "E2015(1,17): Unsatisfied forward declaration of type 'TA<T>'")]
    [InlineData("type IA<K, V> = interface; IA<K> = interface end; begin end.", "E2015(1,17): Unsatisfied forward declaration of type 'IA<K, V>'")]
    [InlineData("type TA<T> = class; type TA<T> = class end; begin end.", "E2015(1,17): Unsatisfied forward declaration of type 'TA<T>'",
        "E2004(1,37): Identifier redeclared: 'TA'")]
    [InlineData("type TOut = class type TA<T> = class; end; begin end.", "E2015(1,35): Unsatisfied forward declaration of type 'TA<T>'")]
    [InlineData("type TA<T> = class; TA<T> = record end; begin end.", "E2001(1,32): Declaration of 'TA' differs from previous declaration")]
    [InlineData("type TA<T> = class; TA<T> = interface end; begin end.", "E2001(1,32): Declaration of 'TA' differs from previous declaration")]
    public void ForwardGeneric_Unsatisfied_E2015(string rest, params string[] expected) =>
        Assert.Equal(expected, BindDiagnostics("program P; " + rest).Where(d => !d.StartsWith('H')));

    private const string TypeOperandProgram = """
        program P;
        type
          TBox<T> = class
            V: T;
            class var Count: Integer;
          end;
          IBox<T> = interface
            ['{11111111-2222-3333-4444-555555555555}']
          end;
          TPair<A, B> = record
            X: A;
            Y: B;
          end;
          EGen<T> = class
          end;
          TList<T> = class
          public
            type
              TNode = record
                V: T;
              end;
          end;
        procedure Take(const G: TGUID);
        begin
        end;
        function Has(const Instance: TObject; const IID: TGUID): Boolean;
        begin
          Result := Instance <> nil;
        end;
        var
          O: TObject; W: Word; R: TPair<Byte, Byte>; G: TGUID; I: IInterface; B: Boolean; N: Integer; C: TClass; Q: Pointer;
        begin
        {0}
        end.
        """;

    /// <summary>LANG-784: an instance name — plain and unit-qualified — in each of the six operand positions.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-784")]
    [InlineData("B := O is {Q}TBox<Integer>; O := O as {Q}TBox<Integer>; B := O is {Q}IBox<Integer>; I := I as {Q}IBox<Integer>;", "")]
    [InlineData("B := O is {Q}TBox<Integer>; O := O as {Q}TBox<Integer>; B := O is {Q}IBox<Integer>; I := I as {Q}IBox<Integer>;", "P.")]
    [InlineData("N := {Q}TBox<Integer>(O).V; {Q}TBox<Integer>(O).V := 1; R := {Q}TPair<Byte, Byte>(W); I := {Q}IBox<Integer>(Q); Q := Pointer({Q}TBox<Integer>(O));", "")]
    [InlineData("N := {Q}TBox<Integer>(O).V; {Q}TBox<Integer>(O).V := 1; R := {Q}TPair<Byte, Byte>(W); I := {Q}IBox<Integer>(Q); Q := Pointer({Q}TBox<Integer>(O));", "P.")]
    [InlineData("try except on E: {Q}EGen<Integer> do O := E; on {Q}EGen<string> do ; end;", "")]
    [InlineData("try except on E: {Q}EGen<Integer> do O := E; on {Q}EGen<string> do ; end;", "P.")]
    [InlineData("Take({Q}IBox<Integer>); G := {Q}IBox<Integer>; B := Has(O, {Q}IBox<Integer>);", "")]
    [InlineData("Take({Q}IBox<Integer>); G := {Q}IBox<Integer>; B := Has(O, {Q}IBox<Integer>);", "P.")]
    [InlineData("C := {Q}TBox<Integer>; O := {Q}TBox<Integer>.Create; N := {Q}TBox<Integer>.Count; B := O.ClassType = {Q}TBox<Integer>;", "")]
    [InlineData("C := {Q}TBox<Integer>; O := {Q}TBox<Integer>.Create; N := {Q}TBox<Integer>.Count; B := O.ClassType = {Q}TBox<Integer>;", "P.")]
    [InlineData("Q := TypeInfo({Q}TBox<Integer>); N := SizeOf({Q}TPair<Byte, Word>) + SizeOf({Q}TList<Integer>.TNode); R := Default({Q}TPair<Byte, Byte>);", "")]
    [InlineData("Q := TypeInfo({Q}TBox<Integer>); N := SizeOf({Q}TPair<Byte, Word>) + SizeOf({Q}TList<Integer>.TNode); R := Default({Q}TPair<Byte, Byte>);", "P.")]
    public void InstanceAsTypeOperand(string statements, string qualifier)
    {
        var program = TypeOperandProgram.Replace("{0}", statements.Replace("{Q}", qualifier));
        Assert.Equal([], CompileDiagnostics(program).Where(d => !d.StartsWith('H') && !d.StartsWith('W')));
    }

    /// <summary>LANG-785: an invocation whose callee denotes a type is a typecast with the typecast diagnostics, never a routine call.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-785")]
    [InlineData("N := TBox<Integer>(O).V;", "")]
    [InlineData("N := P.TBox<Integer>(O).V;", "")]
    [InlineData("R := TPair<Byte, Byte>(W);", "")]
    [InlineData("N := TBox<Integer>(O, O).V;", "E2900(33,6): Feature not supported: typecast")]
    [InlineData("N := TBox<Integer>().V;", "E2900(33,6): Feature not supported: typecast")]
    [InlineData("R := TPair<Byte, Byte>(N);", "E2041(33,6): Variable typecast not allowed for managed types or types of different size")]
    [InlineData("I := IBox<Integer>(I);", "E2040(33,6): Invalid typecast from 'IInterface' to 'IBox<Integer>'")]
    [InlineData("I := P.IBox<Integer>(Q);", "")]
    public void InvocationOfGenericType_IsATypecast(string statement, string expected)
    {
        var diagnostics = CompileDiagnostics(TypeOperandProgram.Replace("{0}", statement)).Where(d => !d.StartsWith('H') && !d.StartsWith('W'));
        Assert.Equal(expected.Length == 0 ? [] : [expected], diagnostics);
    }

    /// <summary>"CODE(line,col): message" of a program compiled against the real <c>System</c>.</summary>
    private static string[] CompileDiagnostics(string program)
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        var model = Compilation.Create(options, [], fs).GetSemanticModel(@"C:\p\P.pas");
        return Format(model.GetDiagnostics());
    }
}
