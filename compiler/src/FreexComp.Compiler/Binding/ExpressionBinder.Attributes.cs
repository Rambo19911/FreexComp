using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Attribute constructors (LANG-761, C-126).
internal sealed partial class ExpressionBinder
{
    /// <summary>
    /// LANG-761: <c>[Name(args)]</c> as <c>TNameAttribute.Create(args)</c> — overload resolution over the class's constructors named
    /// <c>Create</c>; every argument must be a constant expression (E2050).
    /// </summary>
    public BoundExpression BindAttributeConstructor(AttributeSyntax syntax, ClassTypeSymbol type)
    {
        var candidates = MemberCandidates(type, "Create");
        if (candidates.IsEmpty)
        {
            _diagnostics.ReportUndeclared(syntax.Name, "Create");
            return Error(syntax);
        }

        var call = BindMethodCall(syntax, candidates, ReceiverKind.ClassName, instance: null, type, syntax.ArgumentList);
        if (call.Method is { MethodKind: not MethodKind.Constructor })
        {
            _diagnostics.Report(syntax, "E2010", call.Method.Name, type.Name);
            return Error(syntax, [.. call.Arguments]);
        }

        foreach (var (argument, value) in (syntax.ArgumentList?.Arguments ?? default).Zip(call.Arguments))
        {
            if (!value.HasErrors && value.ConstantValue is null && !IsConstantConversion(value))
            {
                _diagnostics.Report(argument.Expression, "E2050");
                return Error(syntax, [.. call.Arguments]);
            }
        }

        return MethodCallExpression(syntax, call);
    }

    private static bool IsConstantConversion(BoundExpression value) =>
        value is BoundConversionExpression { Operand: var operand } && (operand.ConstantValue is not null || IsConstantConversion(operand));
}
