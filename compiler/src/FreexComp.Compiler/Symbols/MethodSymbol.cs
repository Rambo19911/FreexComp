using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Symbols;

public enum MethodKind { Ordinary, Constructor, Destructor, ClassMethod, ClassStatic }

/// <summary>
/// A method of a class (LANG-620…624, api.md §5). <see cref="RoutineSymbol.Parameters"/> are the declared parameters; the
/// implicit ones come first in IR (ABI-082): <see cref="SelfParameter"/> (instance, or the VMT for class methods, ABI-084) and,
/// for constructors and destructors, <see cref="FlagParameter"/> (<c>Alloc</c> / <c>Dealloc</c>, ABI-075/076). A constructor's
/// <see cref="RoutineSymbol.ReturnType"/> is its class (ABI-075: it returns the instance) without a <c>Result</c> variable.
/// </summary>
public sealed class MethodSymbol : RoutineSymbol
{
    /// <param name="containingType">A class, or an interface (C-098: interface methods have no body; <see cref="VmtSlot"/> is the IMT slot).</param>
    internal MethodSymbol(string name, TypeSymbol containingType, ImmutableArray<Location> locations, MethodDeclarationSyntax declaration,
        MethodKind methodKind, Visibility visibility)
        : base(SymbolKind.Method, name, containingType, locations, visibility)
    {
        MethodDeclaration = declaration;
        MethodKind = methodKind;
    }

    public MethodKind MethodKind { get; }

    /// <summary>The declaring class; throws for an interface method (see <see cref="ContainingInterface"/>).</summary>
    public ClassTypeSymbol ContainingType => (ClassTypeSymbol)ContainingSymbol!;

    /// <summary>The declaring interface of an interface method (LANG-660); null for class methods.</summary>
    public InterfaceTypeSymbol? ContainingInterface => ContainingSymbol as InterfaceTypeSymbol;

    /// <summary>Introduces a VMT slot (<c>virtual</c> or <c>dynamic</c>, ABI-074).</summary>
    public bool IsVirtual { get; internal set; }

    public bool IsOverride { get; internal set; }

    public bool IsAbstract { get; internal set; }

    public bool IsFinal { get; internal set; }

    public bool IsReintroduce { get; internal set; }

    /// <summary>The VMT slot (ABI-073/074) of a virtual or overriding method; -1 for a statically bound one.</summary>
    public int VmtSlot { get; internal set; } = -1;

    /// <summary>The ancestor method an <c>override</c> replaces.</summary>
    public MethodSymbol? OverriddenMethod { get; internal set; }

    /// <summary>The id of a <c>message N</c> handler (LANG-622, ABI §7.2 <c>MsgTable</c>, C-113); null for other methods.</summary>
    public int? MessageId { get; internal set; }

    /// <summary>Calls dispatch through the VMT (LANG-622).</summary>
    public bool IsDispatched => VmtSlot >= 0;

    public bool IsClassMethod => MethodKind is MethodKind.ClassMethod or MethodKind.ClassStatic;

    /// <summary>The declaration in the class body.</summary>
    internal MethodDeclarationSyntax MethodDeclaration { get; }

    /// <summary><c>Self</c> (LANG-621); null for <c>class … static</c> methods.</summary>
    public ParameterSymbol? SelfParameter { get; internal set; }

    /// <summary>The hidden <c>Alloc</c> (constructor) or <c>Dealloc</c> (destructor) flag, an <c>i8</c> (ABI-075/076).</summary>
    internal ParameterSymbol? FlagParameter { get; set; }

    /// <summary>The leading IR parameters before the hidden <c>Result</c> and the declared ones (ABI-082).</summary>
    internal override ImmutableArray<ParameterSymbol> ImplicitParameters =>
        [.. new[] { SelfParameter, FlagParameter }.OfType<ParameterSymbol>()];

    /// <summary><c>procedure TFoo.Bar(A: Integer)</c> (CMP-731).</summary>
    public override string ToDisplayString()
    {
        var keyword = MethodKind switch
        {
            MethodKind.Constructor => "constructor",
            MethodKind.Destructor => "destructor",
            _ => (IsClassMethod ? "class " : "") + (ReturnType is null ? "procedure" : "function"),
        };
        var parameters = Parameters.IsEmpty ? "" : "(" + string.Join("; ", Parameters.Select(p => $"{ParameterSymbol.Prefix(p.Mode)}{p.Name}: {p.Type.Name}")) + ")";
        var result = ReturnType is not null && MethodKind is not MethodKind.Constructor ? ": " + ReturnType.Name : "";
        return $"{keyword} {ContainingSymbol!.Name}.{Name}{parameters}{result}";
    }
}
