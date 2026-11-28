// The semantic model's lookups are synchronous index reads over an already bound compilation; the optional token is left out on purpose.
#pragma warning disable xUnit1051

using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-355: the language service's open instance of a generic (over its own type parameters) and the method bodies bound on it stay out
/// of the compilation: no instance list, use, diagnostic, IR or <c>.pcu</c> sees them.
/// </summary>
public sealed partial class GenericBindingTests
{
    private const string OpenUnit = """
        unit U;
        interface
        type
          TStack<T> = class
          public
            type
              TNode = class
              public
                Value: T;
                Next: TNode;
              end;
          public
            Top: TNode;
            class var Made: Integer;
            procedure Push(const V: T);
            function Peek: TStack<T>;
          end;
          TBox<T> = class
          public
            Value: T;
            class var Count: Integer;
          end;
          TUtil = class
          public
            class function Max<T>(const A, B: T): T;
          end;
          TKeep<T: TUtil; C: constructor> = class
          public
            Item: T;
            function Larger(const A, B: T): T;
          end;
        implementation
        class function TUtil.Max<T>(const A, B: T): T;
        begin
          Result := A;
        end;
        function TKeep<T, C>.Larger(const A, B: T): T;
        begin
          Result := TUtil.Max<T>(A, B);
          Item.Free;
          C.Create.Free;
        end;
        procedure TStack<T>.Push(const V: T);
        var
          S: TStack<T>;
        begin
          S := Self;
          if V = V then
            S.Top := nil;
        end;
        function TStack<T>.Peek: TStack<T>;
        begin
          Result := Self;
        end;
        end.
        """;

    private const string OpenProgram = """
        program P;
        uses U;
        var
          B: TBox<Integer>;
        begin
          B := TBox<Integer>.Create;
          Writeln(TBox<Integer>.Count);
          B.Free;
        end.
        """;

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void OpenInstance_LeavesInstancesDiagnosticsIrAndPcuUnchanged()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", OpenProgram).Add(@"C:\p\U.pas", OpenUnit).AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        (string, string) Ll(Compilation compilation)
        {
            var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
            return (fs.ReadAllText(@"C:\p\obj\P.ll"), fs.ReadAllText(@"C:\p\obj\U.ll"));
        }

        var fresh = Compilation.Create(options, [], fs);
        var ir = Ll(fresh);
        var pcu = fresh.PcuTexts();

        var compilation = Compilation.Create(options, [], fs);
        var diagnostics = compilation.GetDiagnostics().Select(d => d.ToText()).ToList();
        var model = compilation.GetSemanticModel(@"C:\p\U.pas");
        var generics = model.Unit.InterfaceMembers.OfType<GenericTypeSymbol>().ToDictionary(g => g.Name);
        var stack = generics["TStack"];
        var box = generics["TBox"];

        Assert.NotNull(stack.OpenInstance);                                          // the model made it, and bound a body on it
        // C-358: a generic method called with an open instance's type parameter gets an instance for navigation only
        var max = model.Unit.InterfaceMembers.OfType<ClassTypeSymbol>().Single(c => c.Name == "TUtil").GenericMethods.Single();
        var call = model.SyntaxTree.FindToken(model.SyntaxTree.Text.Text.IndexOf("Max<T>(A, B)", StringComparison.Ordinal));
        Assert.Same(max, Assert.IsType<MethodSymbol>(model.GetSymbolInfo(call.Parent!).Symbol).GenericMethodDefinition);
        Assert.Empty(max.Instances);
        // C-422: and its own open instance (over its own type parameter), on which the model binds its body; none reaches code generation
        Assert.NotNull(max.OpenInstance);
        Assert.Equal(2, max.OpenInstances.Count);
        Assert.Single(max.OpenInstances.Values, i => ReferenceEquals(i, max.OpenInstance));
        Assert.Single(max.OpenInstances.Values, i => ReferenceEquals(i, model.GetSymbolInfo(call.Parent!).Symbol));
        Assert.NotNull(generics["TKeep"].OpenInstance);
        var top = model.SyntaxTree.FindToken(model.SyntaxTree.Text.Text.IndexOf("Top := nil", StringComparison.Ordinal));
        Assert.Same(stack.OpenInstance, Assert.IsType<FieldSymbol>(model.GetSymbolInfo(top.Parent!).Symbol).ContainingSymbol);
        Assert.Empty(stack.Instances);
        Assert.All(box.Instances.Keys, arguments => Assert.DoesNotContain(arguments, a => a.TypeKind == TypeKind.TypeParameter));
        Assert.Single(box.Instances);
        var used = model.Unit.UsedInstances.Concat(compilation.GetSemanticModel(@"C:\p\P.pas").Unit.UsedInstances).OfType<TypeSymbol>();
        Assert.DoesNotContain(used, t => t.TypeArguments.Any(a => a.TypeKind == TypeKind.TypeParameter) || ReferenceEquals(t, stack.OpenInstance));
        // C-378: the released instance class vars are those of the class constructor items of the modules
        var released = new[] { model.Unit, compilation.GetSemanticModel(@"C:\p\P.pas").Unit }
            .SelectMany(u => Compiler.Lowering.Lowerer.ClassConstructorItems(u)).SelectMany(i => i.ReleasedClassVars);
        Assert.DoesNotContain(released, v => v.ContainingSymbol is TypeSymbol { TypeArguments: var a }
            && a.Any(t => t.TypeKind == TypeKind.TypeParameter));
        Assert.Equal(diagnostics, compilation.GetDiagnostics().Select(d => d.ToText()));
        Assert.DoesNotContain(model.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);   // V = V on a type parameter: dropped

        Assert.Equal(ir, Ll(compilation));
        Assert.Equal(pcu, compilation.PcuTexts());
    }
}
