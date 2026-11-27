// The semantic model's lookups are synchronous index reads over an already bound compilation; the optional token is left out on purpose.
#pragma warning disable xUnit1051

using FreexComp.Compiler.Syntax;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-416: the type queries inside generics (intrinsics.md LANG-976) — constants of each instance after substitution, values that are
/// not constant in the language service's open instance.
/// </summary>
public sealed partial class GenericBindingTests
{
    private const string TypeQueryGeneric = """
          TInfo<T> = class
          public
            class function Managed: Boolean;
            class function Kind: TTypeKind;
            class function Fixed: Boolean;
          end;
        """;

    private const string TypeQueryMethods = """
        class function TInfo<T>.Managed: Boolean;
        const
          M = IsManagedType(T);
        begin
          Result := M;
          Result := IsManagedType(T) or HasWeakRef(T);
          Result := IsManagedType(TArray<T>);
        end;
        class function TInfo<T>.Kind: TTypeKind;
        const
          K = GetTypeKind(T);
        begin
          Result := K;
          Result := GetTypeKind(T);
          case GetTypeKind(T) of
            GetTypeKind(T): Result := tkUnknown;
          end;
        end;
        class function TInfo<T>.Fixed: Boolean;
        begin
          Result := IsManagedType(string);
          Result := IsConstValue(SizeOf(T));
        end;
        """;

    /// <summary>LANG-976: one generic, two instances — every type query on <c>T</c> is the constant of that instance.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-976")]
    public void TypeQueries_FoldPerInstance()
    {
        var result = BindWithTypeKinds($$"""
            program P;
            type
              TArray<T> = array of T;
            {{TypeQueryGeneric}}
            {{TypeQueryMethods}}
            var
              B: Boolean;
              K: TTypeKind;
            begin
              B := TInfo<Integer>.Managed;
              K := TInfo<Integer>.Kind;
              B := TInfo<string>.Managed;
              K := TInfo<string>.Kind;
            end.
            """);

        Assert.Equal([], Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
        string Body(string instance, string method) =>
            BoundDump.Of(result.Routines.Single(r => r.Routine.Name == method && r.Routine.ContainingSymbol!.Name == instance).Body);

        Assert.Equal(
            "(block (:= Result:Boolean False:Boolean) (:= Result:Boolean False:Boolean) (:= Result:Boolean True:Boolean))",
            Body("TInfo<Integer>", "Managed"));
        Assert.Equal(
            "(block (:= Result:Boolean True:Boolean) (:= Result:Boolean True:Boolean) (:= Result:Boolean True:Boolean))",
            Body("TInfo<string>", "Managed"));
        Assert.Equal(
            "(block (:= Result:TTypeKind 1:TTypeKind) (:= Result:TTypeKind 1:TTypeKind) "
            + "(case 1:TTypeKind (arm [1] (:= Result:TTypeKind 0:TTypeKind))))",
            Body("TInfo<Integer>", "Kind"));
        Assert.Equal(
            "(block (:= Result:TTypeKind 9:TTypeKind) (:= Result:TTypeKind 9:TTypeKind) "
            + "(case 9:TTypeKind (arm [9] (:= Result:TTypeKind 0:TTypeKind))))",
            Body("TInfo<string>", "Kind"));
    }

    /// <summary>
    /// LANG-976: in the open instance a type query whose operand depends on a type parameter is a value of its result type and no
    /// constant; one that does not is the constant it is everywhere. The compilation's diagnostics do not change.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-976")]
    public void TypeQueries_OpenInstance_AreNotConstant()
    {
        var unit = $$"""
            unit U;
            interface
            type
            {{TypeQueryGeneric}}
            implementation
            {{TypeQueryMethods}}
            end.
            """;
        var fs = new InMemoryFileSystem().Add(@"C:\p\U.pas", unit).AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\U.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        var compilation = Compilation.Create(options, [], fs);
        var diagnostics = compilation.GetDiagnostics().Select(d => d.ToText()).ToList();
        Assert.DoesNotContain(diagnostics, d => d.Contains("error", StringComparison.Ordinal));

        var model = compilation.GetSemanticModel(@"C:\p\U.pas");
        (string? Constant, string? Type) Query(string text, string? within = null)
        {
            var context = within ?? text;
            var token = model.SyntaxTree.FindToken(unit.IndexOf(context, StringComparison.Ordinal) + context.IndexOf(text, StringComparison.Ordinal));
            var call = token.Parent!.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().First();
            return (model.GetConstantValue(call)?.ToDisplayString(), model.GetTypeInfo(call).Type?.Name);
        }

        Assert.Equal((null, "Boolean"), Query("IsManagedType(T) or"));
        Assert.Equal((null, "Boolean"), Query("HasWeakRef(T)"));
        Assert.Equal((null, "Boolean"), Query("IsManagedType(TArray<T>)"));
        Assert.Equal((null, "TTypeKind"), Query("GetTypeKind(T)", within: "Result := GetTypeKind(T)"));
        Assert.Equal(("True", "Boolean"), Query("IsManagedType(string)"));       // no type parameter in the operand
        Assert.Equal(("True", "Boolean"), Query("IsConstValue(SizeOf(T))"));     // SizeOf(T) is a constant there as before
        Assert.Equal(diagnostics, compilation.GetDiagnostics().Select(d => d.ToText()));
        Assert.DoesNotContain(model.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }
}
