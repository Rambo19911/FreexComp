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

    /// <summary>The declaring record of a record method (LANG-730, C-122); null for class and interface methods.</summary>
    public RecordTypeSymbol? ContainingRecord => ContainingSymbol as RecordTypeSymbol;

    /// <summary>The declaring class, record or interface.</summary>
    public TypeSymbol Owner => (TypeSymbol)ContainingSymbol!;

    /// <summary>
    /// A record constructor (LANG-730, C-122): bound as a static method returning the record; its <c>Result</c> is zero-filled
    /// before the body runs and is <c>Self</c> inside it. <see cref="MethodKind"/> is <see cref="MethodKind.ClassStatic"/>.
    /// </summary>
    public bool IsRecordConstructor { get; internal set; }

    /// <summary>The LANG-731 operator name of a <c>class operator</c> (<c>Add</c>, <c>Implicit</c>…, C-123); null for other methods.</summary>
    public string? OperatorName { get; internal set; }

    /// <summary>
    /// An anonymous method (LANG-720, CMP-380): the method <c>$anon&lt;n&gt;</c> of a closure class; <c>Self</c> is the closure of
    /// <see cref="AnonymousParent"/>.
    /// </summary>
    public bool IsAnonymous { get; internal set; }

    /// <summary>The routine (or anonymous method) whose body contains this anonymous method; null for a unit-level body.</summary>
    public RoutineSymbol? AnonymousParent { get; internal set; }

    /// <summary>
    /// The number of <c>dispid N</c> (com-interfaces.md LANG-694): required on a member of a dispatch interface, information only
    /// on a method of an ordinary interface; null without it.
    /// </summary>
    public int? DispId { get; internal set; }

    /// <summary>
    /// LANG-697: the <c>Invoke</c> flags of a member of a dispatch interface — 1 a method, 2 a property get, 4 a property put,
    /// 8 a property put by reference (the last three: the hidden accessors of a dispatch property); 0 for every other method.
    /// A call of such a method is a dispatch call (CMP-1208).
    /// </summary>
    internal int DispatchFlags { get; set; }

    /// <summary>
    /// CMP-1207 (C-302): the <c>implements</c> property a synthesised <c>$dlg.…</c> method delegates to; null for other methods.
    /// </summary>
    internal PropertySymbol? DelegateProperty { get; set; }

    /// <summary>The generic method this method is an instance of (LANG-701, C-117); null otherwise.</summary>
    public GenericMethodSymbol? GenericMethodDefinition { get; internal set; }

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

    /// <summary><c>procedure TFoo.Bar(A: Integer); virtual</c> (CMP-731).</summary>
    public override string ToDisplayString()
    {
        var keyword = MethodKind switch
        {
            _ when IsRecordConstructor => "constructor",
            _ when OperatorName is not null => "class operator",
            MethodKind.Constructor => "constructor",
            MethodKind.Destructor => "destructor",
            _ => (IsClassMethod ? "class " : "") + (ReturnType is null ? "procedure" : "function"),
        };
        var parameters = Parameters.IsEmpty ? "" : "(" + string.Join("; ", Parameters.Select(ParameterSymbol.Declaration)) + ")";
        var result = ReturnType is not null && MethodKind is not MethodKind.Constructor && !IsRecordConstructor ? ": " + ReturnType.Name : "";
        var directives = (IsOverride ? "; override" : IsVirtual ? "; virtual" : "") + (IsAbstract ? "; abstract" : "") + (IsFinal ? "; final" : "")
            + (DispId is { } dispId ? "; dispid " + dispId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");     // CMP-1211
        return $"{keyword} {ContainingSymbol!.Name}.{Name}{parameters}{result}{directives}";
    }
}
