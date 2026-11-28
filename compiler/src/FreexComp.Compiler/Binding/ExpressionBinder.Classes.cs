using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>A bound method call before it becomes an expression or a statement; <see cref="Method"/> null after an error.</summary>
internal readonly record struct MethodCall(MethodSymbol? Method, ImmutableArray<BoundExpression> Arguments, BoundReceiver? Receiver, bool IsNothing = false);

// Members of classes (C-080…C-082): fields, method calls with their receiver (CMP-367), Self, inherited (LANG-621, 624).
internal sealed partial class ExpressionBinder
{
    private enum ReceiverKind { Implicit, Instance, ClassName, Inherited }

    /// <summary>The method whose body (or a routine nested in it) is being bound.</summary>
    private MethodSymbol? CurrentMethod
    {
        get
        {
            // an anonymous method sees the method its body is written in (LANG-721: Self is captured)
            for (var routine = _routine; routine != null; routine = ClosureInfo.LexicalParent(routine))
            {
                if (routine is MethodSymbol { IsAnonymous: false } method)
                {
                    return method;
                }
            }

            return null;
        }
    }

    /// <summary><c>Self</c> of the current method (captured when referenced from a nested routine, CMP-345).</summary>
    private BoundExpression SelfOf(MethodSymbol method, SyntaxNode syntax)
    {
        if (method is { IsRecordConstructor: true, ResultVariable: { } constructed })
        {
            NoteReference(constructed);
            return new BoundVariableExpression(syntax, constructed);              // LANG-730: Self is the constructed result
        }

        var self = method.SelfParameter!;
        NoteReference(self);
        CheckCapture(self, syntax);
        return new BoundParameterExpression(syntax, self);
    }

    /// <summary>The method has an instance <c>Self</c>: an instance method, or a record constructor (LANG-730).</summary>
    private static bool HasInstanceSelf(MethodSymbol method) =>
        method is { SelfParameter: not null, IsClassMethod: false } or { IsRecordConstructor: true, ResultVariable: not null };

    /// <summary>The unit whose code is being bound (the owner of the nearest unit scope).</summary>
    private UnitSymbol? CurrentUnit
    {
        get
        {
            for (var scope = _scope; scope != null; scope = scope.Parent)
            {
                if (scope is { Kind: ScopeKind.Unit, Owner: UnitSymbol unit })
                {
                    return unit;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// LANG-610/612: whether <paramref name="member"/> of <paramref name="owner"/> may be used here through a receiver whose static
    /// type is <paramref name="receiver"/> (the current class for implicit <c>Self</c>); E2610 otherwise. <c>private</c> and
    /// <c>protected</c> are unit-wide; <c>strict private</c> needs a method of the class itself; (strict) protected also opens
    /// to methods of a descendant D when the receiver is D or a descendant of D.
    /// </summary>
    private bool CheckAccess(SyntaxNode at, Symbol member, TypeSymbol owner, TypeSymbol? receiver)
    {
        var visibility = member.Visibility;
        if (visibility is Visibility.Public or Visibility.Published)
        {
            return true;
        }

        member.IsReferenced = true;                                                     // H2003 (C-096)
        var current = CurrentMethod?.Owner;
        var sameUnit = TypeMembers.Outermost(owner).ContainingSymbol is UnitSymbol unit && ReferenceEquals(unit, CurrentUnit);
        // LANG-774 (C-344): code of a nested type counts as code of every enclosing type
        var selves = current is null ? [] : new[] { current }.Concat(TypeMembers.EnclosingTypes(current)).ToList();
        var own = selves.Contains(owner);
        var descendant = !own && selves.OfType<ClassTypeSymbol>().Any(currentClass => currentClass.SelfAndAncestors().Contains(owner)
            && receiver is ClassTypeSymbol r && r.SelfAndAncestors().Contains(currentClass));
        var allowed = visibility switch
        {
            Visibility.Private => sameUnit,
            Visibility.StrictPrivate => own,
            Visibility.Protected => sameUnit || own || descendant || LocalDescendant(owner, receiver),
            _ => own || descendant,                                                     // strict protected
        };
        if (!allowed)
        {
            var text = visibility switch
            {
                Visibility.Private => "private",
                Visibility.StrictPrivate => "strict private",
                Visibility.Protected => "protected",
                _ => "strict protected",
            };
            _diagnostics.Report(at, "E2610", text, member.Name, TypeMembers.DisplayName(owner));       // LANG-775: TOuter.TInner
        }

        return allowed;
    }

    /// <summary>
    /// C-260: (non-strict) protected members of <paramref name="owner"/> are open to code anywhere in a unit that declares a
    /// descendant of <paramref name="owner"/>, when the receiver's static type is that descendant or one of its descendants
    /// (<c>TAccess(B).Member</c> with <c>TAccess = class(TBase)</c> declared in the current unit).
    /// </summary>
    private bool LocalDescendant(TypeSymbol owner, TypeSymbol? receiver)
    {
        if (receiver is not ClassTypeSymbol receiverClass || CurrentUnit is not { } unit)
        {
            return false;
        }

        foreach (var type in receiverClass.SelfAndAncestors())
        {
            if (ReferenceEquals(type, owner))
            {
                return false;                                                   // reached the owner without a local descendant
            }

            if (ReferenceEquals(TypeMembers.Outermost(type).ContainingSymbol, unit) && type.SelfAndAncestors().Contains(owner))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A <c>class var</c> (LANG-615, CMP-370): the class's global, after the visibility check against <paramref name="receiver"/>.</summary>
    private BoundExpression ClassVariable(SyntaxNode syntax, SyntaxNode at, VariableSymbol classVar, TypeSymbol? receiver) =>
        CheckAccess(at, classVar, (TypeSymbol)classVar.ContainingSymbol!, receiver) ? new BoundVariableExpression(syntax, classVar) : Error(syntax);

    /// <summary>A field named inside a method (LANG-621): <c>Self.F</c>.</summary>
    private BoundExpression ImplicitField(IdentifierNameSyntax name, FieldSymbol field)
    {
        if (CurrentMethod is not { } method || !HasInstanceSelf(method))
        {
            _diagnostics.Report(name, "E2608", field.Name);
            return Error(name);
        }

        if (field.ContainingSymbol is TypeSymbol owner && !CheckAccess(name, field, owner, method.Owner))
        {
            return Error(name);
        }

        return new BoundFieldAccessExpression(name, SelfOf(method, name), field);
    }

    /// <summary>
    /// LANG-441 over the inheritance chain: the methods named <paramref name="name"/> of the nearest class declaring that name,
    /// and of further ancestors while all of them carry <c>overload</c>; empty when the nearest declaration is no method. The generic
    /// methods of the name count for that rule too (<see cref="MethodsNamed"/>): these are the non-generic methods of that set.
    /// </summary>
    internal static ImmutableArray<RoutineSymbol> MemberCandidates(ClassTypeSymbol type, string name) => MethodsNamed(type, name).Methods;

    /// <summary>
    /// LANG-980 rule 1 (C-419): the methods named <paramref name="name"/> a call through <paramref name="type"/> can mean — the set of
    /// one type is its non-generic methods together with its generic methods of that name; for a class the sets of the ancestors are
    /// added while every method of the name carries <c>overload</c> (LANG-441). A record has its own methods only.
    /// </summary>
    internal static MethodSet MethodsNamed(TypeSymbol type, string name)
    {
        var candidates = ImmutableArray.CreateBuilder<RoutineSymbol>();
        var generics = ImmutableArray.CreateBuilder<GenericMethodSymbol>();
        if (type is RecordTypeSymbol record)
        {
            candidates.AddRange(record.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, name)));
            generics.AddRange(record.GenericMethods.Where(g => IdentifierComparer.Instance.Equals(g.Name, name)));
        }

        foreach (var owner in (type as ClassTypeSymbol)?.SelfAndAncestors() ?? [])
        {
            if (owner.Members.Any(m => m is not MethodSymbol && IdentifierComparer.Instance.Equals(m.Name, name)))
            {
                break;                                                          // a field, class var or property hides the methods
            }

            var methods = owner.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, name)).ToList();
            var generic = owner.GenericMethods.Where(g => IdentifierComparer.Instance.Equals(g.Name, name)).ToList();
            // an ancestor's method that a nearer one overrides or hides with the same parameters is not a second candidate
            candidates.AddRange(methods.Where(m => !candidates.Any(c => SameParameters(c, m))));
            generics.AddRange(generic);
            if (methods.Any(m => !m.IsOverload) || generic.Any(g => !g.IsOverload))
            {
                break;
            }
        }

        return new MethodSet(candidates.ToImmutable(), generics.ToImmutable());
    }

    /// <summary>The non-generic and the generic methods of one name (<see cref="MethodsNamed"/>).</summary>
    internal readonly record struct MethodSet(ImmutableArray<RoutineSymbol> Methods, ImmutableArray<GenericMethodSymbol> Generics);

    /// <summary>LANG-441 over the interface chain (C-098): as <see cref="MemberCandidates"/> for the methods of interfaces.</summary>
    private static ImmutableArray<RoutineSymbol> InterfaceCandidates(InterfaceTypeSymbol type, string name)
    {
        var candidates = ImmutableArray.CreateBuilder<RoutineSymbol>();
        foreach (var owner in type.SelfAndAncestors())
        {
            var methods = owner.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, name)).ToList();
            candidates.AddRange(methods.Where(m => !candidates.Any(c => SameParameters(c, m))));
            if (methods.Count > 0 && methods.Any(m => !m.IsOverload))
            {
                break;
            }
        }

        return candidates.ToImmutable();
    }

    private static bool SameParameters(RoutineSymbol a, RoutineSymbol b) =>
        a.Parameters.Length == b.Parameters.Length
        && a.Parameters.Zip(b.Parameters).All(p => p.First.Mode == p.Second.Mode && ReferenceEquals(p.First.Type, p.Second.Type));

    /// <summary>Binds the arguments, picks the method and determines its receiver (CMP-367 table).</summary>
    /// <param name="generic">
    /// LANG-979, 980 (C-418, C-419): for a name with generic methods, the arguments the inference already bound and what makes the
    /// chosen candidate the instance (<see cref="GenericCandidates"/>).
    /// </param>
    private MethodCall BindMethodCall(SyntaxNode call, ImmutableArray<RoutineSymbol> candidates, ReceiverKind kind, BoundExpression? instance,
        ClassTypeSymbol? className, ArgumentListSyntax? argumentList, GenericCandidates? generic = null)
    {
        var (routine, arguments) = BindCall(call, candidates, argumentList, generic);
        if (routine is not MethodSymbol method)
        {
            return new MethodCall(null, arguments, null);
        }

        if (TypeMembers.EnclosingInstance(method.Owner) is { } genericOwner)
        {
            GenericUses.Note(genericOwner, GenericUses.Current ?? GenericUses.UnitOf(_scope), call);      // C-116: the caller's unit emits it (CMP-385)
        }

        var receiverType = kind switch
        {
            ReceiverKind.Instance => instance!.Type is ClassRefTypeSymbol reference ? reference.Class : instance.Type,
            ReceiverKind.ClassName => className,
            _ => CurrentMethod?.Owner,
        };
        if (method.ContainingInterface is null && !CheckAccess(call, method, method.Owner, receiverType))
        {
            return new MethodCall(null, arguments, null);
        }

        var receiver = Receiver(call, method, kind, instance, className);
        return new MethodCall(receiver is null ? null : method, arguments, receiver);
    }

    /// <summary>
    /// LANG-650/651 (C-087): <c>o is T</c> / <c>c is T</c> (a class reference) / <c>o as T</c> with <c>T</c> a class name. E2603 when
    /// <c>T</c> or the left operand is no class (interfaces: M2.5), E2641 when the classes are unrelated.
    /// </summary>
    private BoundExpression BindTypeTest(BinaryExpressionSyntax binary, bool isAs)
    {
        var operand = BindExpression(binary.Left);
        var target = TypeOperand(binary.Right);                                // LANG-771, 784 (C-344, C-346): TOuter.TInner, TBox<Integer>
        if (operand.HasErrors || target is ErrorTypeSymbol)
        {
            return Error(binary, operand);
        }

        if (target is InterfaceTypeSymbol intf)
        {
            return BindInterfaceTest(binary, operand, intf, isAs);
        }

        if (target is not ClassTypeSymbol type)
        {
            _diagnostics.Report(binary.Right, "E2603");
            return Error(binary, operand);
        }

        var source = operand.Type switch
        {
            ClassTypeSymbol c => c,
            ClassRefTypeSymbol r when !isAs => r.Class,
            _ => null,
        };
        if (source is null)
        {
            _diagnostics.Report(binary.Left, "E2603");
            return Error(binary, operand);
        }

        if (!source.InheritsFrom(type) && !type.InheritsFrom(source))
        {
            _diagnostics.Report(binary, "E2641", source.Name, type.Name);
            return Error(binary, operand);
        }

        return new BoundTypeTestExpression(binary, operand, type, isAs, isAs ? type : _types.Boolean);
    }

    /// <summary>
    /// LANG-650/651 (C-100): <c>o is I</c> (an object), <c>o as I</c> / <c>i as I</c> (an object or an interface) with <c>I</c> an
    /// interface that has a GUID (E2640); any other left operand is E2603. The run-time test is always made (CMP-373).
    /// </summary>
    private BoundExpression BindInterfaceTest(BinaryExpressionSyntax binary, BoundExpression operand, InterfaceTypeSymbol target, bool isAs)
    {
        if (operand.Type is not (ClassTypeSymbol or InterfaceTypeSymbol) || operand.Type is InterfaceTypeSymbol && !isAs)
        {
            _diagnostics.Report(binary.Left, "E2603");
            return Error(binary, operand);
        }

        if (target.Guid is null)
        {
            _diagnostics.Report(binary.Right, "E2640", target.Name);
            return Error(binary, operand);
        }

        return new BoundTypeTestExpression(binary, operand, target, isAs, isAs ? target : _types.Boolean);
    }

    /// <summary>The VMT of an instance (<c>o.ClassType</c>, CMP-367: <c>load ptr, ptr %o</c>) as a class reference value.</summary>
    private static BoundExpression ClassOf(SyntaxNode syntax, BoundExpression instance) =>
        new BoundDereferenceExpression(syntax, instance, ((ClassTypeSymbol)instance.Type).Reference);

    /// <summary>
    /// LANG-620/653 and CMP-367 for class methods: <c>Self</c> is the class reference — the VMT of a class name, a class-reference
    /// value, the VMT of an instance, or the current <c>Self</c>; virtual class methods dispatch except on a class name and with
    /// <c>inherited</c>. Static class methods have no <c>Self</c>.
    /// </summary>
    private BoundReceiver? ClassMethodReceiver(SyntaxNode call, MethodSymbol method, ReceiverKind kind, BoundExpression? instance, ClassTypeSymbol? className)
    {
        if (method.MethodKind == MethodKind.ClassStatic)
        {
            return BoundReceiver.Static;
        }

        switch (kind)
        {
            case ReceiverKind.ClassName:
                return new BoundReceiver(new BoundClassReferenceExpression(QualifierOf(call), className!, className!.Reference), IsVirtual: false, Flag: null);
            case ReceiverKind.Instance when instance!.Type is ClassRefTypeSymbol:
                return new BoundReceiver(instance, IsVirtual: method.IsDispatched, Flag: null);
            case ReceiverKind.Instance:
                return new BoundReceiver(ClassOf(call, instance!), IsVirtual: method.IsDispatched, Flag: null);
        }

        var current = CurrentMethod;
        if (current is null)
        {
            _diagnostics.Report(call, "E2608", method.Name);
            return null;
        }

        if (kind == ReceiverKind.Implicit && method.Owner is ClassTypeSymbol owner && !TypeMembers.SelfAndAncestors(current.Owner).Contains(owner)
            && TypeMembers.EnclosingTypes(current.Owner).OfType<ClassTypeSymbol>().FirstOrDefault(e => e.SelfAndAncestors().Contains(owner)) is { } enclosing)
        {
            // LANG-773 (C-344): a class method of an enclosing type named in a nested type's method is called as through that type
            return new BoundReceiver(new BoundClassReferenceExpression(call, enclosing, enclosing.Reference), IsVirtual: false, Flag: null);
        }

        var inherited = kind == ReceiverKind.Inherited;
        if (inherited && method.IsAbstract)
        {
            _diagnostics.Report(call, "E2626", method.Name);                         // LANG-624
            return null;
        }

        BoundExpression self = current switch
        {
            { SelfParameter: null } => new BoundClassReferenceExpression(call, current.ContainingType, current.ContainingType.Reference),
            { IsClassMethod: true } => SelfOf(current, call),
            _ => ClassOf(call, SelfOf(current, call)),
        };
        return new BoundReceiver(self, IsVirtual: !inherited && method.IsDispatched && current.SelfParameter is not null, Flag: null);
    }

    /// <summary>
    /// C-407: the class name a call is made on (<c>TFoo</c> in <c>TFoo.Create</c>, <c>TFoo.Make(1)</c>) — the syntax of the receiver's
    /// class reference, so that the call's own node keeps the call's type in the semantic model; the call itself where it has no
    /// qualifier (an attribute).
    /// </summary>
    private static SyntaxNode QualifierOf(SyntaxNode call) => call switch
    {
        MemberAccessExpressionSyntax access => access.Expression,
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } => access.Expression,
        _ => call,
    };

    private BoundReceiver? Receiver(SyntaxNode call, MethodSymbol method, ReceiverKind kind, BoundExpression? instance, ClassTypeSymbol? className)
    {
        if (method.IsClassMethod)
        {
            return ClassMethodReceiver(call, method, kind, instance, className);
        }

        // a record helper for the class-reference type itself (LANG-990 (b), C-442): the class reference is the helped value
        if (kind == ReceiverKind.Instance && instance!.Type is ClassRefTypeSymbol && method.ContainingSymbol is not HelperTypeSymbol { IsClassHelper: false })
        {
            if (method.MethodKind != MethodKind.Constructor)
            {
                _diagnostics.Report(call, "E2607");
                return null;
            }

            // LANG-630/633: on a class-reference value — allocates; a virtual constructor dispatches on the run-time class
            return new BoundReceiver(instance, IsVirtual: method.IsDispatched, Flag: 1);
        }

        if (kind == ReceiverKind.Implicit && method.MethodKind == MethodKind.Constructor && CurrentMethod is { IsClassMethod: true, SelfParameter: not null } classMethod)
        {
            return new BoundReceiver(SelfOf(classMethod, call), IsVirtual: method.IsDispatched, Flag: 1);   // Create inside a class method
        }

        switch (kind)
        {
            case ReceiverKind.ClassName when method.MethodKind == MethodKind.Constructor && className!.IsAbstract:
                _diagnostics.Report(call, "E2600", className.Name);                    // LANG-602
                return null;
            case ReceiverKind.ClassName when method.MethodKind == MethodKind.Constructor:
                if (className!.VirtualSlots.FirstOrDefault(s => s.IsAbstract) is { } slot)
                {
                    _diagnostics.Report(call, "W1061", className.Name, slot.ContainingType.Name, slot.Name);   // LANG-622
                }

                // LANG-630/633: on a class name — allocates (Alloc = 1), bound statically to that class's constructor
                return new BoundReceiver(new BoundClassReferenceExpression(QualifierOf(call), className!, className!.Reference), IsVirtual: false, Flag: 1);
            case ReceiverKind.ClassName:
                _diagnostics.Report(call, "E2607");
                return null;
            case ReceiverKind.Instance:
                return HelperReceiver(call, method, Dispatched(method, instance!, inherited: false));
        }

        var current = CurrentMethod;
        if (current is null || !HasInstanceSelf(current))
        {
            _diagnostics.Report(call, "E2608", method.Name);
            return null;
        }

        if (kind == ReceiverKind.Inherited && method.IsAbstract)
        {
            _diagnostics.Report(call, "E2626", method.Name);                         // LANG-624
            return null;
        }

        return HelperReceiver(call, method, Dispatched(method, SelfOf(current, call), inherited: kind == ReceiverKind.Inherited));
    }

    /// <summary>
    /// CMP-367: a constructor on an instance runs the body only (Alloc = 0); a destructor passes Dealloc = 1 (0 for
    /// <c>inherited</c>); virtual methods — virtual constructors included (C-256, LANG-630/633) — dispatch through the VMT of
    /// the instance's class except with <c>inherited</c> (LANG-624).
    /// </summary>
    private static BoundReceiver Dispatched(MethodSymbol method, BoundExpression self, bool inherited) => method.MethodKind switch
    {
        MethodKind.Constructor => new BoundReceiver(self, IsVirtual: !inherited && method.IsDispatched, Flag: 0),
        MethodKind.Destructor => new BoundReceiver(self, IsVirtual: !inherited && method.IsDispatched, Flag: inherited ? 0 : 1),
        _ => new BoundReceiver(self, IsVirtual: !inherited && method.IsDispatched, Flag: null),
    };

    /// <summary>A method call as a value (E2034 for a procedure).</summary>
    private BoundExpression MethodCallExpression(SyntaxNode call, MethodCall method)
    {
        if (method.Method is null)
        {
            return Error(call, [.. method.Arguments]);
        }

        if (method.Method.ReturnType is null)
        {
            _diagnostics.Report(call, "E2034");
            return Error(call, [.. method.Arguments]);
        }

        // a constructor called on a class name yields that class, on an instance the instance's type (LANG-630)
        var type = method.Method.MethodKind == MethodKind.Constructor ? method.Receiver!.Self switch
        {
            BoundClassReferenceExpression reference => reference.Class,
            { Type: ClassTypeSymbol instance } => instance,
            { Type: ClassRefTypeSymbol reference } => reference.Class,         // LANG-633: the static class of the reference
            _ => null,
        } : null;
        return new BoundCallExpression(call, method.Method, method.Arguments, type: type) { Receiver = method.Receiver };
    }

    /// <summary>A method named without qualifier inside a method: <c>Self.M(args)</c>; inside <c>with o do</c> one of <c>o</c>'s (C-109).</summary>
    private BoundExpression ImplicitMethodCall(SyntaxNode call, IdentifierNameSyntax name, ArgumentListSyntax? argumentList) =>
        MethodCallExpression(call, ImplicitMethod(call, name, argumentList));

    /// <summary>
    /// C-222 (LANG-701): <c>Max&lt;Integer&gt;(…)</c> or <c>Max(…)</c> inside a method of a type (or descendant) declaring the generic
    /// method <c>Max</c> — called as <c>Self.Max</c> (class and static methods as through the class). A bare name only when no local,
    /// parameter or member of that name hides it. Null when this is no such call.
    /// </summary>
    private MethodCall? ImplicitGenericMethod(SyntaxNode call, ExpressionSyntax callee, ArgumentListSyntax? argumentList)
    {
        if (callee is not SimpleNameSyntax { Identifier.IsMissing: false } name || CurrentMethod is not { } current)
        {
            return null;
        }

        var text = name.Identifier.Value as string ?? name.Identifier.Text;
        // a non-generic method of the name does not hide the generic ones: they are one overload set (LANG-980, C-419)
        if (name is IdentifierNameSyntax && (_withs.Count > 0
            || _scope.DeclaringScope(text) is { Kind: not (ScopeKind.BuiltIn or ScopeKind.Unit or ScopeKind.Uses) } declaring
            && !(declaring.Kind == ScopeKind.TypeMember && declaring.LookupLocal(text) is MethodSymbol)))
        {
            return null;
        }

        if (GenericMethodCandidates(current.Owner, name, name, argumentList) is not { } candidates)
        {
            return null;
        }

        return candidates.IsEmpty ? new MethodCall(null, [.. candidates.Bound], null)
            : BindMethodCall(call, candidates.Routines, ReceiverKind.Implicit, null, null, argumentList, candidates);
    }

    private MethodCall ImplicitMethod(SyntaxNode call, IdentifierNameSyntax name, ArgumentListSyntax? argumentList)
    {
        var text = name.Identifier.Value as string ?? name.Identifier.Text;
        if (_scope.For(name).Lookup(text) is { } symbol && WithOf(text, symbol) is { Type: ClassTypeSymbol type } with)
        {
            return BindMethodCall(call, MemberCandidates(type, text), ReceiverKind.Instance, WithReceiver(name, with), null, argumentList);
        }

        if (_scope.For(name).Lookup(text) is MethodSymbol { ContainingRecord: not null } recordMethod && WithOf(text, recordMethod) is { Type: RecordTypeSymbol record } recordWith)
        {
            // C-122: a record method named inside 'with r do' is r.M (LANG-375, 730)
            ImmutableArray<RoutineSymbol> methods = [.. record.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, text))];
            return BindMethodCall(call, methods, ReceiverKind.Instance, WithReceiver(name, recordWith), null, argumentList);
        }

        return BindMethodCall(call, WithoutReplaced(_scope.For(name).LookupCandidates(text)), ReceiverKind.Implicit, null, null, argumentList);
    }

    /// <summary>
    /// C-210 (LANG-441): as in <see cref="MemberCandidates"/>, an ancestor's method that a nearer candidate overrides or hides with
    /// the same parameters is not a second candidate (the scope lists the nearest class's methods first).
    /// </summary>
    private static ImmutableArray<RoutineSymbol> WithoutReplaced(ImmutableArray<RoutineSymbol> candidates)
    {
        var kept = ImmutableArray.CreateBuilder<RoutineSymbol>();
        foreach (var candidate in candidates)
        {
            if (candidate is MethodSymbol { ContainingSymbol: ClassTypeSymbol } method
                && kept.Any(k => k is MethodSymbol { ContainingSymbol: ClassTypeSymbol } nearer && !ReferenceEquals(nearer.ContainingType, method.ContainingType)
                    && nearer.ContainingType.SelfAndAncestors().Contains(method.ContainingType)
                    && SameParameters(nearer, method)))
            {
                continue;
            }

            kept.Add(candidate);
        }

        return kept.ToImmutable();
    }

    // ------------------------------------------------------------------ with (LANG-375, C-109)

    /// <summary>The active <c>with</c> designators, outermost first: their member scope, hidden variable and designator type.</summary>
    private readonly List<WithContext> _withs = [];

    internal readonly record struct WithContext(Scope Scope, VariableSymbol Variable, TypeSymbol Type);

    /// <summary>Binds <paramref name="bind"/> with the members of a <c>with</c> designator in scope (the scope is entered by the caller).</summary>
    public T InWith<T>(WithContext with, Func<T> bind)
    {
        _withs.Add(with);
        try
        {
            return InScope(with.Scope, bind);
        }
        finally
        {
            _withs.RemoveAt(_withs.Count - 1);
        }
    }

    /// <summary>The innermost active <c>with</c> whose member scope declares <paramref name="symbol"/> as <paramref name="name"/>.</summary>
    private WithContext? WithOf(string name, Symbol symbol)
    {
        for (var i = _withs.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_withs[i].Scope.LookupLocal(name), symbol))
            {
                return _withs[i];
            }
        }

        return null;
    }

    /// <summary>The designator as bound through its hidden variable: <c>$with^</c> for a record held by address, else <c>$with</c>.</summary>
    private static BoundExpression WithReceiver(SyntaxNode syntax, WithContext with) =>
        with.Variable.Type is PointerTypeSymbol && with.Type is RecordTypeSymbol record
            ? new BoundDereferenceExpression(syntax, new BoundVariableExpression(syntax, with.Variable), record)
            : new BoundVariableExpression(syntax, with.Variable);

    /// <summary>
    /// LANG-375: <paramref name="member"/> named inside <c>with d do</c> = <c>d.Member</c> — a record field, an object's field, class var,
    /// property or (parameterless) method call, with the visibility checks of an explicit member access.
    /// </summary>
    private BoundExpression WithMember(IdentifierNameSyntax name, WithContext with, Symbol member)
    {
        var receiver = WithReceiver(name, with);
        switch (member)
        {
            case FieldSymbol field when with.Type is ClassTypeSymbol type:
                return field.ContainingSymbol is TypeSymbol owner && !CheckAccess(name, field, owner, type) ? Error(name) : new BoundFieldAccessExpression(name, receiver, field);
            case FieldSymbol field:
                return new BoundFieldAccessExpression(name, receiver, field);
            case VariableSymbol classVar:
                return ClassVariable(name, name, classVar, with.Type);
            case PropertySymbol property:
                return CheckAccess(name, property, property.Owner, with.Type) ? PropertyAccess(name, property, property.IsClassProperty ? null : receiver) : Error(name);
            case MethodSymbol:
                return ImplicitMethodCall(name, name, argumentList: null);
            case var nested when NestedValue(name, nested) is { } value:
                return value;
            default:
                return Error(name);
        }
    }

    /// <summary>
    /// <c>x.Name</c> where <c>x</c> is a class name (constructors, CMP-367 <c>TFoo.Create</c>), an object reference (fields and methods,
    /// LANG-604) or a record. A method sets <paramref name="methodCall"/> (with <paramref name="argumentList"/>) and returns null;
    /// anything else returns the bound member access.
    /// </summary>
    private BoundExpression? BindMemberCall(MemberAccessExpressionSyntax syntax, SyntaxNode call, ArgumentListSyntax? argumentList, out MethodCall? methodCall)
    {
        methodCall = null;
        if (syntax.Name.Identifier.IsMissing)
        {
            if (syntax.Expression is GenericNameSyntax || syntax.Expression.DescendantNodes().OfType<GenericNameSyntax>().Any())
            {
                _ = TypeNamedBy(syntax.Expression);                     // C-347 (CMP-1394): G<X>. being typed — the instance exists for completion
                return Error(syntax);
            }

            // CMP-1421 (C-401): `A[1].`, `P^.`, `F.` being typed — the receiver is kept (bound without diagnostics) so completion knows its type
            return ProbeBinder(new DiagnosticBag()).BindExpression(syntax.Expression) is { HasErrors: false } typed ? Error(syntax, typed) : Error(syntax);
        }

        var name = syntax.Name.Identifier.Value as string ?? syntax.Name.Identifier.Text;
        var qualifierType = syntax.Expression is PredefinedTypeExpressionSyntax
            ? _types.String                                                         // LANG-997 (b) (C-444): string.Join(…)
            : TypeNamedBy(syntax.Expression);
        if (qualifierType is ErrorTypeSymbol || qualifierType is not null && IncompleteInstance(syntax.Expression, qualifierType))
        {
            return Error(syntax);                                                   // C-116: a generic name already reported
        }

        if (qualifierType is not null and not HelperTypeSymbol && HelperFor(qualifierType, name) is { } typeHelper)
        {
            return HelperMember(syntax, call, argumentList, typeHelper, instance: null, name, out methodCall);   // C-124: Integer.Parse
        }

        if (qualifierType is DynamicArrayTypeSymbol dynamicArray && IdentifierComparer.Instance.Equals(name, "Create"))
        {
            return BindDynArrayCreate(argumentList is null ? syntax : call, argumentList, dynamicArray);   // LANG-271 (C-388): TInts.Create(1, 2)
        }

        if (qualifierType is HelperTypeSymbol helperName && TypeMembers.MemberNestedMember(helperName, name) is { } helperNested
            && NestedValue(syntax, helperNested) is { } helperValue)
        {
            return helperValue;                                                     // C-363: THelper.Limit (LANG-770, 774)
        }

        if (qualifierType is RecordTypeSymbol recordName)
        {
            return StaticRecordMember(syntax, call, argumentList, recordName, name, out methodCall);     // C-122 (LANG-730)
        }

        if (qualifierType is ClassTypeSymbol className)
        {
            if (GenericMethodCandidates(className, syntax, argumentList) is { } genericMethod)
            {
                methodCall = genericMethod.IsEmpty ? null
                    : BindMethodCall(call, genericMethod.Routines, ReceiverKind.ClassName, null, className, argumentList, genericMethod);
                return genericMethod.IsEmpty ? Error(syntax, genericMethod.Bound) : null;   // C-117 (LANG-701, 979, 980)
            }

            var candidates = MemberCandidates(className, name);
            switch (className.GetMembers(name).FirstOrDefault())
            {
                case PropertySymbol { IsClassProperty: true } property:                 // LANG-647
                    return CheckAccess(syntax.Name, property, property.Owner, className) ? PropertyAccess(syntax, property, self: null) : Error(syntax);
                case VariableSymbol classVar:                                           // LANG-615
                    return ClassVariable(syntax, syntax.Name, classVar, className);
                case var nested when nested is not null && NestedValue(syntax, nested) is { } value:
                    return value;                                                       // LANG-770: TOuter.Limit
            }

            if (candidates.IsEmpty)
            {
                if (TypeMembers.MemberNestedType(className, name) is { } nestedType)
                {
                    _diagnostics.Report(syntax, "E2009", nestedType.Name);              // a nested type is not a value (C-311)
                }
                else if (className.GetMembers(name).Any())
                {
                    _diagnostics.Report(syntax.Name, "E2607");
                }
                else
                {
                    _diagnostics.Report(syntax.Name, "E2005", className.Name, name);
                }

                return Error(syntax);
            }

            methodCall = BindMethodCall(call, candidates, ReceiverKind.ClassName, null, className, argumentList);
            return null;
        }

        var operand = BindOperand(syntax.Expression);
        var receiver = Called(operand);
        if (!ReferenceEquals(receiver, operand) && HelperFor(operand.Type, name) is not null)
        {
            // LANG-990 (b) (C-442): a value of a function type without parameters is the receiver itself when the active helper
            // of its type declares the name; otherwise it is called (LANG-482) and the member is looked up in the result
            receiver = operand;
        }

        if (receiver.HasErrors)
        {
            return Error(syntax, receiver);
        }

        if (receiver.Type is TypeParameterTypeSymbol parameter && ConstraintReceiver(parameter, name) is { } constraint)
        {
            // C-364: in an open instance (the language service only) a value of a type parameter has its constraints' members (LANG-702)
            receiver = new BoundConversionExpression(syntax.Expression, receiver, constraint, ConversionKind.ImplicitReference, isExplicit: false, constantValue: null);
        }

        if (HelperFor(receiver.Type, name) is { } helper)
        {
            return HelperMember(syntax, call, argumentList, helper, receiver, name, out methodCall);         // LANG-752 (C-124)
        }

        if (ReferenceEquals(receiver.Type, _types.Char) && receiver.ConstantValue is { Value: BigInteger code }
            && HelperFor(_types.String, name) is { } stringHelper)
        {
            // LANG-998 (b) (C-444): a character constant without the member for Char is the one-character string
            var text = new BoundLiteral(receiver.Syntax, new ConstantValue(((char)(int)code).ToString(), _types.String));
            return HelperMember(syntax, call, argumentList, stringHelper, text, name, out methodCall);
        }

        if (receiver.Type is ClassTypeSymbol type)
        {
            if (GenericMethodCandidates(type, syntax, argumentList) is { } genericMethod)
            {
                methodCall = genericMethod.IsEmpty ? null
                    : BindMethodCall(call, genericMethod.Routines, ReceiverKind.Instance, receiver, null, argumentList, genericMethod);
                return genericMethod.IsEmpty ? Error(syntax, [receiver, .. genericMethod.Bound]) : null;      // C-117
            }

            var candidates = MemberCandidates(type, name);
            if (!candidates.IsEmpty)
            {
                methodCall = BindMethodCall(call, candidates, ReceiverKind.Instance, receiver, null, argumentList);
                return null;
            }

            switch (type.GetMembers(name).FirstOrDefault())
            {
                case FieldSymbol field:
                    if (field.ContainingSymbol is TypeSymbol owner && !CheckAccess(syntax.Name, field, owner, type))
                    {
                        return Error(syntax, receiver);
                    }

                    return new BoundFieldAccessExpression(syntax, receiver, field);
                case VariableSymbol classVar:
                    return ClassVariable(syntax, syntax.Name, classVar, type);
                case PropertySymbol property:
                    if (!CheckAccess(syntax.Name, property, property.Owner, type))
                    {
                        return Error(syntax, receiver);
                    }

                    // LANG-647: class property accessors are class vars or static class methods — no Self
                    return PropertyAccess(syntax, property, property.IsClassProperty ? null : receiver);
            }

            _diagnostics.Report(syntax.Name, "E2005", type.Name, name);
            return Error(syntax, receiver);
        }

        if (receiver.Type is InterfaceTypeSymbol intf)
        {
            // LANG-664 (C-098): a method of the interface or an ancestor, called through the IMT (ABI-093)
            var candidates = InterfaceCandidates(intf, name);
            if (!candidates.IsEmpty)
            {
                methodCall = BindMethodCall(call, candidates, ReceiverKind.Instance, receiver, null, argumentList);
                return null;
            }

            if (intf.SelfAndAncestors().SelectMany(i => i.Properties).FirstOrDefault(p => IdentifierComparer.Instance.Equals(p.Name, name)) is { } property)
            {
                return PropertyAccess(syntax, property, receiver);                  // LANG-664 (C-101): accessor calls through the IMT
            }

            _diagnostics.Report(syntax.Name, "E2005", intf.Name, name);
            return Error(syntax, receiver);
        }

        if (receiver.Type is ClassRefTypeSymbol reference)
        {
            // LANG-652: constructors, class methods, class vars and class properties through a class-reference value
            var target = reference.Class;
            switch (target.GetMembers(name).FirstOrDefault())
            {
                case PropertySymbol { IsClassProperty: true } property:
                    return CheckAccess(syntax.Name, property, property.Owner, target) ? PropertyAccess(syntax, property, self: null) : Error(syntax, receiver);
                case VariableSymbol classVar:
                    return ClassVariable(syntax, syntax.Name, classVar, target);
            }

            if (GenericMethodCandidates(target, syntax, argumentList) is { } genericMethod)
            {
                // LANG-980 rule 7 (C-419): the generic class methods too, as through the class name
                methodCall = genericMethod.IsEmpty ? null
                    : BindMethodCall(call, genericMethod.Routines, ReceiverKind.Instance, receiver, null, argumentList, genericMethod);
                return genericMethod.IsEmpty ? Error(syntax, [receiver, .. genericMethod.Bound]) : null;
            }

            var candidates = MemberCandidates(target, name);
            if (candidates.IsEmpty)
            {
                if (target.GetMembers(name).Any())
                {
                    _diagnostics.Report(syntax.Name, "E2607");
                }
                else
                {
                    _diagnostics.Report(syntax.Name, "E2005", target.Name, name);
                }

                return Error(syntax, receiver);
            }

            methodCall = BindMethodCall(call, candidates, ReceiverKind.Instance, receiver, null, argumentList);
            return null;
        }

        var record = receiver.Type is PointerTypeSymbol { PointedType: RecordTypeSymbol pointed }
            ? new BoundDereferenceExpression(syntax.Expression, receiver, pointed) : receiver;        // p.M = p^.M (extended syntax)
        if (record.Type is RecordTypeSymbol { Methods.IsEmpty: false } or RecordTypeSymbol { Properties.IsEmpty: false }
            or RecordTypeSymbol { ClassVariables.IsEmpty: false } or RecordTypeSymbol { NestedMembers.IsEmpty: false }
            or RecordTypeSymbol { GenericMethods.IsEmpty: false })
        {
            return RecordInstanceMember(syntax, call, argumentList, record, name, out methodCall);   // C-122 (LANG-730)
        }

        return RecordMember(syntax, receiver);
    }

    /// <summary>
    /// C-364: the constraint (class, interface, else <c>TObject</c> for <c>class</c> / <c>constructor</c>) through which a member
    /// <paramref name="name"/> of a value of <paramref name="parameter"/> is bound — the first that has it, else the first; null without one.
    /// </summary>
    private TypeSymbol? ConstraintReceiver(TypeParameterTypeSymbol parameter, string name)
    {
        var types = parameter.MemberTypes(_types).ToList();
        return types.FirstOrDefault(t => t switch
        {
            ClassTypeSymbol c => !MemberCandidates(c, name).IsEmpty || c.GetMembers(name).Any(),
            InterfaceTypeSymbol i => !InterfaceCandidates(i, name).IsEmpty || i.SelfAndAncestors().SelectMany(a => a.Properties).Any(p => IdentifierComparer.Instance.Equals(p.Name, name)),
            _ => false,
        }) ?? types.FirstOrDefault();
    }

    private TypeSymbol? TypeNamedBy(ExpressionSyntax expression)
    {
        var type = NestedNames.AccessibleTypeNamedBy(_scope, expression, GenericSegment, _diagnostics, _types);   // LANG-780 (C-345), LANG-774 (C-344)
        if (type is not null && _types.HasTypeHints
            && _scope.Unqualified(Unparenthesized(expression)) is IdentifierNameSyntax { Identifier: { IsMissing: false } token } name)
        {
            HintChecks.CheckName(_diagnostics, _types, _scope, name, token.Value as string ?? token.Text, type);    // LANG-472 (C-232)
        }

        return type;
    }

    /// <summary>
    /// The candidates of a call whose name has generic methods (<see cref="GenericMethodCandidates(TypeSymbol, MemberAccessExpressionSyntax, ArgumentListSyntax?)"/>):
    /// the non-generic methods of the name and the instances of the generic ones (LANG-980) — empty after an error — and, by argument
    /// position, the arguments the inference bound (LANG-979 rule 1: that binding is the one the call uses; null where the argument is
    /// bound later, against the parameters of the candidates).
    /// </summary>
    private sealed record GenericCandidates(ImmutableArray<RoutineSymbol> Routines, ImmutableArray<BoundExpression?> Prebound)
    {
        public bool IsEmpty => Routines.IsEmpty;

        /// <summary>The arguments bound so far: the children of the error expression of a call that failed.</summary>
        public BoundExpression[] Bound => Prebound.IsDefault ? [] : [.. Prebound.OfType<BoundExpression>()];

        /// <summary>
        /// LANG-980 rule 5: the name has several methods, so the call is resolved as an overloaded one also when one candidate is left
        /// (the generic methods that are not inferable are no candidates): E2123 when it does not take the arguments.
        /// </summary>
        public bool Overloaded { get; init; }

        /// <summary>
        /// LANG-980 rule 6: what the call does with the candidate it chose — an instance of a generic method is only then made the
        /// instance and recorded as used (<c>DeclarationBinder.UseMethod</c>); the result is the routine the call binds to.
        /// </summary>
        public Func<RoutineSymbol, RoutineSymbol> Chosen { get; init; } = routine => routine;
    }

    /// <summary>
    /// C-117 (LANG-701), C-418 (LANG-979), C-419 (LANG-980): the candidates of a call of <c>syntax.Name</c> through <paramref name="type"/>
    /// when the methods of that name (<see cref="MethodsNamed"/>) include a generic one. With a type-argument list: the generic methods
    /// with that number of type parameters, for the written arguments (E2703 for none; E2704). Without one: the non-generic methods
    /// and the instance of every generic method that is inferable by unification of its parameter types with the argument types and
    /// whose inferred arguments satisfy its constraints; when that leaves nothing, E2711 (no generic method inferable), the E2704 of
    /// the only inferable one, else E2123. Nothing more when an argument has an error of its own. Null when the name has no generic
    /// method; empty after an error.
    /// </summary>
    private GenericCandidates? GenericMethodCandidates(TypeSymbol type, MemberAccessExpressionSyntax syntax, ArgumentListSyntax? argumentList) =>
        GenericMethodCandidates(type, syntax.Name, syntax, argumentList);

    /// <param name="nameSyntax">The method name as written (<c>Max</c> or <c>Max&lt;Integer&gt;</c>).</param>
    /// <param name="at">The member access <c>TUtil.Max</c>, or the name itself for an unqualified call (C-222).</param>
    private GenericCandidates? GenericMethodCandidates(TypeSymbol type, SimpleNameSyntax nameSyntax, ExpressionSyntax at, ArgumentListSyntax? argumentList)
    {
        var name = nameSyntax.Identifier.Value as string ?? nameSyntax.Identifier.Text;
        var set = MethodsNamed(type, name);
        if (set.Generics.IsEmpty || (_scope.Owner ?? GenericUses.UnitOf(_scope)) is not { } container)
        {
            return null;
        }

        // LANG-980 rule 6: an instance is a candidate only; the call makes the one it chooses the instance (with what its heading reported)
        var declarations = new DeclarationBinder(_types, _scope, container, _diagnostics);
        var pending = new Dictionary<RoutineSymbol, (DeclarationBinder.MethodCandidate Candidate, DiagnosticBag? Reported)>(ReferenceEqualityComparer.Instance);
        RoutineSymbol Chosen(RoutineSymbol routine)
        {
            if (!pending.TryGetValue(routine, out var chosen))
            {
                return routine;
            }

            foreach (var diagnostic in chosen.Reported?.ToImmutable() ?? [])
            {
                _diagnostics.Add(diagnostic);
            }

            return declarations.UseMethod(chosen.Candidate, at);
        }

        var routines = ImmutableArray.CreateBuilder<RoutineSymbol>();
        if (nameSyntax is GenericNameSyntax written)
        {
            // rule 2: the generic methods with that number of type parameters (C-226: each of them)
            var count = written.TypeArgumentList.Arguments.Count;
            var fitting = set.Generics.Where(d => d.ParameterNames.Length == count).ToList();
            if (fitting.Count == 0)
            {
                _diagnostics.Report(nameSyntax, "E2703", name, set.Generics[0].ParameterNames.Length);
                return new GenericCandidates([], default);
            }

            ImmutableArray<TypeSymbol> arguments = [.. written.TypeArgumentList.Arguments.Select(declarations.BindTypeArgument)];
            if (arguments.Any(a => a.TypeKind == TypeKind.Error))
            {
                return new GenericCandidates([], default);
            }

            foreach (var generic in fitting)
            {
                if (declarations.CandidateMethod(generic, arguments, at) is { } candidate)
                {
                    pending[candidate.Instance] = (candidate, null);
                    routines.Add(candidate.Instance);
                }
            }

            return new GenericCandidates(routines.Count == fitting.Count ? routines.ToImmutable() : [], default) { Chosen = Chosen };
        }

        // LANG-979 (C-418): the parameter types as declared are those of each method's open instance (null: its heading does not bind)
        var opens = set.Generics.Select(d => DeclarationBinder.OpenInstanceOf(_types, d)).ToList();
        var prebound = BindInferenceArguments(opens, set.Methods, argumentList);
        if (prebound.Any(a => a is { HasErrors: true }))
        {
            return new GenericCandidates([], prebound);                             // LANG-979 rule 5: the argument's diagnostic, nothing else
        }

        // rule 3: the non-generic methods, and the instance of every inferable generic method whose arguments satisfy its constraints
        routines.AddRange(set.Methods);
        var inference = new InferenceArguments(this, argumentList, prebound);
        var inferable = 0;
        DiagnosticBag? refused = null;
        for (var d = 0; d < set.Generics.Length; d++)
        {
            if (opens[d] is not { } open || GenericInference.Infer(open, i => inference.At(i, open)) is not { } inferred)
            {
                continue;
            }

            inferable++;
            var reported = new DiagnosticBag();
            if (new DeclarationBinder(_types, _scope, container, reported).CandidateMethod(set.Generics[d], inferred, at) is { } candidate
                && !reported.ToImmutable().Any(r => r.Severity == DiagnosticSeverity.Error))
            {
                pending[candidate.Instance] = (candidate, reported);
                routines.Add(candidate.Instance);
            }
            else
            {
                refused = reported;                                                 // no candidate, and no diagnostic at this step
            }
        }

        if (routines.Count == 0)
        {
            // rule 5: the methods of the name are all generic and none of them is a candidate
            if (inferable == 0)
            {
                // LANG-978 (C-420): nothing more for a generic constructor, destructor or operator whose heading does not bind as a
                // method of that kind — it was reported where it is declared (E2713)
                if (!set.Generics.Where((g, d) => g.HasRestrictedKind && opens[d] is null).Any())
                {
                    _diagnostics.Report(nameSyntax, "E2711", name);
                }
            }
            else if (inferable == 1 && refused!.ToImmutable() is var reasons && reasons.Any(r => r.Severity == DiagnosticSeverity.Error))
            {
                foreach (var diagnostic in reasons)
                {
                    _diagnostics.Add(diagnostic);                                   // the only inferable one: its E2704, at the method name
                }
            }
            else
            {
                _diagnostics.Report(at, "E2123", name);
            }

            return new GenericCandidates([], prebound);
        }

        return new GenericCandidates(routines.ToImmutable(), prebound) { Overloaded = set.Methods.Length + set.Generics.Length > 1, Chosen = Chosen };
    }

    /// <summary>
    /// LANG-979 rule 1 (CMP-1477 (2)): binds, once for the call and whatever the number of methods of the name, every argument that
    /// has an argument type at a position where a declared parameter type of one of the generic methods (<paramref name="opens"/>)
    /// mentions a type parameter — without an expected type; as a variable where each method of the name (the non-generic
    /// <paramref name="others"/> too) takes it by reference. Null for the others, which the call binds against the parameters of its
    /// candidates as for any method (<c>BindCall</c>): <c>nil</c>, anonymous methods and bracket constructors; arguments of parameters
    /// whose type no type parameter reaches; and arguments whose meaning depends on the parameter type of a method of the name
    /// (<see cref="NeedsParameterType"/>; an interface type identifier, which is its GUID for a <c>TGUID</c> parameter, LANG-680).
    /// </summary>
    private ImmutableArray<BoundExpression?> BindInferenceArguments(IReadOnlyList<MethodSymbol?> opens, ImmutableArray<RoutineSymbol> others, ArgumentListSyntax? argumentList)
    {
        var syntaxes = argumentList?.Arguments ?? default;
        var builder = ImmutableArray.CreateBuilder<BoundExpression?>(syntaxes.Count);
        for (var i = 0; i < syntaxes.Count; i++)
        {
            var expression = syntaxes[i].Expression;
            var parameters = opens.Where(o => o is not null && i < o.Parameters.Length)
                .Select(o => (Symbol: o!.Parameters[i], Generic: GenericInference.Mentions(o.Parameters[i].Type, o.GenericMethodArguments))).ToList();
            var plain = others.Where(m => i < m.Parameters.Length).Select(m => m.Parameters[i]).ToList();
            if (HasNoArgumentType(expression) || !parameters.Any(p => p.Generic)
                || parameters.Any(p => p.Generic && NeedsParameterType(expression, p.Symbol.Type))
                || plain.Any(p => NeedsParameterType(expression, p.Type))
                || plain.Count > 0 && InterfaceNamedBy(expression) is not null)
            {
                builder.Add(null);
                continue;
            }

            builder.Add(parameters.All(p => p.Symbol.IsByReference) && plain.All(p => p.IsByReference) ? BindVariable(expression) : BindExpression(expression));
        }

        return builder.ToImmutable();
    }

    /// <summary>LANG-979 rule 1: <c>nil</c>, an anonymous method or a bracket constructor — bound only against a candidate's parameter type.</summary>
    private static bool HasNoArgumentType(ExpressionSyntax expression) =>
        Unparenthesized(expression) is AnonymousMethodExpressionSyntax or SetConstructorSyntax or LiteralExpressionSyntax { Token.Kind: SyntaxKind.NilKeyword };

    /// <summary>
    /// The argument means something else in the context of a parameter of that type than alone (<see cref="BindExpression(ExpressionSyntax, TypeSymbol?)"/>):
    /// at a procedural, method-pointer or method-reference parameter a routine name is its address and a procedural value is not
    /// called (LANG-480…482); at a dynamic-array parameter <c>+</c> and <c>Concat</c> pass the type to their constructor operands
    /// (LANG-275 rule 3, LANG-279). Such an argument is bound against the parameter of the candidates, as before C-418.
    /// </summary>
    private bool NeedsParameterType(ExpressionSyntax expression, TypeSymbol parameter) =>
        GenericInference.IsProceduralLike(parameter)
        || parameter is DynamicArrayTypeSymbol
        && (Unparenthesized(expression) is BinaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.PlusToken } || ConcatIntrinsicCall(expression).Call is not null);

    /// <summary>
    /// What each argument of one call gives the inference of a generic method (<see cref="GenericInference.Argument"/>): the natural
    /// type of a bound argument (at a <c>var</c> / <c>out</c> position the declared type of the variable); for the arguments bound
    /// later a probe on a scratch binder, once per position and form — the elements of a bracket constructor at an open-array
    /// parameter, each alone, and the type of an argument that <see cref="NeedsParameterType"/> (at a procedural, method-pointer or
    /// method-reference parameter without the implicit call). A value that is an untyped literal adapts (C-426,
    /// <see cref="GenericInference.IsLiteral"/>).
    /// </summary>
    private sealed class InferenceArguments(ExpressionBinder binder, ArgumentListSyntax? argumentList, ImmutableArray<BoundExpression?> prebound)
    {
        private enum Form { Constructor, Operand, Value }

        private readonly Dictionary<(int Index, Form Form), GenericInference.Argument> _probed = [];

        public GenericInference.Argument At(int index, MethodSymbol open)
        {
            if (index >= prebound.Length)
            {
                return default;                                                 // a missing argument (a default value)
            }

            var parameter = open.Parameters[index];
            if (prebound[index] is { } bound)
            {
                return parameter.IsByReference ? GenericInference.Argument.Of(bound.Type)
                    : GenericInference.Argument.Value(binder._types, bound.Type, bound.ConstantValue);
            }

            var expression = argumentList!.Arguments[index].Expression;
            var constructor = Unparenthesized(expression) as SetConstructorSyntax;
            if ((constructor is null ? HasNoArgumentType(expression) : parameter.Type is not OpenArrayTypeSymbol)
                || !GenericInference.Mentions(parameter.Type, open.GenericMethodArguments)
                || expression.DescendantNodes().OfType<AnonymousMethodExpressionSyntax>().Any())
            {
                return default;                                                 // an anonymous method is bound once (it makes symbols)
            }

            var form = constructor is not null ? Form.Constructor : GenericInference.IsProceduralLike(parameter.Type) ? Form.Operand : Form.Value;
            if (!_probed.TryGetValue((index, form), out var argument))
            {
                argument = constructor is not null ? GenericInference.Argument.Constructor(Elements(constructor)) : Probe(expression, form);
                _probed[(index, form)] = argument;
            }

            return argument;
        }

        /// <summary>The elements, each bound alone; without a type (<c>default</c>) the one that is a range, has none or does not bind.</summary>
        private List<GenericInference.Argument> Elements(SetConstructorSyntax constructor)
        {
            var elements = new List<GenericInference.Argument>(constructor.Elements.Count);
            foreach (var element in constructor.Elements)
            {
                elements.Add(element.UpperBound is not null || HasNoArgumentType(element.Expression) ? default : Probe(element.Expression, Form.Value));
                if (elements[^1].Type is null)
                {
                    break;                                                      // the constructor gives nothing: the rest is not probed
                }
            }

            return elements;
        }

        private GenericInference.Argument Probe(ExpressionSyntax expression, Form form)
        {
            var diagnostics = new DiagnosticBag();
            var probe = binder.ProbeBinder(diagnostics);
            var bound = form == Form.Operand ? probe.BindOperand(expression) : probe.BindExpression(expression);
            return bound.HasErrors || diagnostics.Count != 0 ? default
                : form == Form.Operand ? GenericInference.Argument.Of(bound.Type)
                : GenericInference.Argument.Value(binder._types, bound.Type, bound.ConstantValue);
        }
    }

    /// <summary>C-116: <c>TList&lt;Integer&gt;</c> in an expression — the instance (bound like the type name, LANG-705/710).</summary>
    private TypeSymbol GenericType(GenericNameSyntax generic) => GenericSegment(generic, null, null);

    /// <summary>
    /// LANG-780 (C-345): a generic segment of a name in an expression — in this scope, a unit qualifier's scope or among an outer
    /// type's nested types; bound once per syntax node (names are probed more than once, their errors are reported once).
    /// </summary>
    private TypeSymbol GenericSegment(GenericNameSyntax generic, Scope? scope, TypeSymbol? outer)
    {
        if (_genericSegments.TryGetValue(generic, out var known))
        {
            return known;
        }

        var type = new DeclarationBinder(_types, _scope, GenericContainer, _diagnostics).BindGenericName(generic, scope, outer);
        _genericSegments[generic] = type;
        return type;
    }

    /// <summary>
    /// LANG-783 (C-346): an instance of a forward generic named before the generic's completion where the complete type is needed
    /// (a member access, <c>SizeOf</c>): E2007 and true.
    /// </summary>
    private bool IncompleteInstance(SyntaxNode at, TypeSymbol type)
    {
        if (type is not (ClassTypeSymbol { IsForward: true, IsGenericInstance: true } or InterfaceTypeSymbol { IsForward: true, IsGenericInstance: true }))
        {
            return false;
        }

        _diagnostics.Report(at, "E2007", type.Name);
        return true;
    }

    /// <summary>
    /// The container of a declaration binder that instantiates for this expression: the scope's owner, else its unit, else the first
    /// owner up the chain (C-346: a generic name binds in every scope; an instance's own container is its generic's).
    /// </summary>
    private Symbol GenericContainer
    {
        get
        {
            if ((_scope.Owner ?? GenericUses.UnitOf(_scope)) is { } container)
            {
                return container;
            }

            for (var scope = _scope.Parent; scope is not null; scope = scope.Parent)
            {
                if (scope.Owner is { } owner)
                {
                    return owner;
                }
            }

            return _types.Error;
        }
    }

    /// <summary>
    /// The type a type operand names (LANG-784, C-346): a type identifier (unit-qualified too, E2003 when undeclared), a generic
    /// instance, a nested type, any of them qualified (LANG-780); null when the expression names no type; the error type after a
    /// reported error.
    /// </summary>
    internal TypeSymbol? TypeOperand(ExpressionSyntax syntax)
    {
        var inner = Unparenthesized(syntax);
        if (_scope.Unqualified(inner) is IdentifierNameSyntax { Identifier: { IsMissing: false } token } name
            && _scope.For(name).Lookup(token.Value as string ?? token.Text) is null)
        {
            _diagnostics.ReportUndeclared(name, token.Value as string ?? token.Text);
            return _types.Error;
        }

        return TypeNamedBy(inner);
    }

    /// <summary>A type written in a statement (a handler type, C-346): bound like a declared type in this scope, errors reported.</summary>
    internal TypeSymbol BindTypeSyntax(TypeSyntax syntax) => new DeclarationBinder(_types, _scope, GenericContainer, _diagnostics).BindTypeArgument(syntax);

    private readonly Dictionary<GenericNameSyntax, TypeSymbol> _genericSegments = new(ReferenceEqualityComparer.Instance);

    /// <summary>The type an expression names, generic segments included (LANG-780); null when it names none.</summary>
    private TypeSymbol? NamedType(ExpressionSyntax expression) => NestedNames.TypeNamedBy(_scope, expression, GenericSegment);

    /// <summary>
    /// A nested constant or enumeration element used as a value (LANG-770); null for other members. A constant of a section this code
    /// may not access is E2610 at the member name (LANG-774, C-361), so is an element of such an enumeration (C-363).
    /// </summary>
    private BoundExpression? NestedValue(SyntaxNode syntax, Symbol member)
    {
        switch (member)
        {
            case ConstantSymbol or EnumMemberSymbol
                when NestedNames.ReportInaccessibleNestedValue(_scope, syntax is MemberAccessExpressionSyntax access ? access.Name : syntax, member, _diagnostics):
                return Error(syntax);
            case ConstantSymbol { IsTyped: false, Value: { } value } constant:
                TypeQueryUses += constant.IsTypeQueryDependent ? 1 : 0;         // LANG-977 (C-417)
                return new BoundConstantExpression(syntax, constant, value);
            case EnumMemberSymbol element:
                return new BoundLiteral(syntax, element.Value);
            default:
                return null;
        }
    }

    /// <summary>
    /// <c>TRec.Name</c> (LANG-730): a constructor or static method (sets <paramref name="methodCall"/>), a class var, a class property
    /// or a nested constant; E2607 for an instance member, E2005 for no member.
    /// </summary>
    private BoundExpression? StaticRecordMember(MemberAccessExpressionSyntax syntax, SyntaxNode call, ArgumentListSyntax? argumentList,
        RecordTypeSymbol record, string name, out MethodCall? methodCall)
    {
        methodCall = null;
        ImmutableArray<RoutineSymbol> candidates = [.. record.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, name))];
        var genericMethod = GenericMethodCandidates(record, syntax, argumentList);
        if (genericMethod is not null)
        {
            candidates = genericMethod.Routines;                                            // C-117; C-419: with the non-generic methods
            if (candidates.IsEmpty)
            {
                return Error(syntax, genericMethod.Bound);
            }
        }

        if (!candidates.IsEmpty)
        {
            methodCall = BindMethodCall(call, candidates, ReceiverKind.ClassName, null, null, argumentList, genericMethod);
            return null;
        }

        switch (record.GetMembers(name).FirstOrDefault())
        {
            case PropertySymbol { IsClassProperty: true } property:
                return CheckAccess(syntax.Name, property, record, record) ? PropertyAccess(syntax, property, self: null) : Error(syntax);
            case VariableSymbol classVar:
                return ClassVariable(syntax, syntax.Name, classVar, record);
            case { } nested when NestedValue(syntax, nested) is { } value:
                return value;
            case { } instanceMember:
                if (instanceMember is TypeSymbol nestedType)
                {
                    _diagnostics.Report(syntax, "E2009", nestedType.Name);
                }
                else
                {
                    _diagnostics.Report(syntax.Name, "E2607");
                }
                return Error(syntax);
            default:
                _diagnostics.Report(syntax.Name, "E2005", record.Name, name);
                return Error(syntax);
        }
    }

    /// <summary><c>r.Name</c> on a record with members (LANG-730): a method (sets <paramref name="methodCall"/>), property, field or class var.</summary>
    private BoundExpression? RecordInstanceMember(MemberAccessExpressionSyntax syntax, SyntaxNode call, ArgumentListSyntax? argumentList,
        BoundExpression record, string name, out MethodCall? methodCall)
    {
        methodCall = null;
        var type = (RecordTypeSymbol)record.Type;
        ImmutableArray<RoutineSymbol> candidates = [.. type.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, name))];
        var genericMethod = GenericMethodCandidates(type, syntax, argumentList);
        if (genericMethod is not null)
        {
            candidates = genericMethod.Routines;                                            // C-117; C-419: with the non-generic methods
            if (candidates.IsEmpty)
            {
                return Error(syntax, [record, .. genericMethod.Bound]);
            }
        }

        if (!candidates.IsEmpty)
        {
            methodCall = BindMethodCall(call, candidates, ReceiverKind.Instance, record, null, argumentList, genericMethod);
            return null;
        }

        switch (type.GetMembers(name).FirstOrDefault())
        {
            case FieldSymbol field:
                return CheckAccess(syntax.Name, field, type, type) ? new BoundFieldAccessExpression(syntax, record, field) : Error(syntax, record);
            case PropertySymbol property:
                return CheckAccess(syntax.Name, property, type, type)
                    ? PropertyAccess(syntax, property, property.IsClassProperty ? null : record) : Error(syntax, record);
            case VariableSymbol classVar:
                return ClassVariable(syntax, syntax.Name, classVar, type);
            case { } nested when NestedValue(syntax, nested) is { } value:
                return value;
            default:
                _diagnostics.Report(syntax.Name, "E2005", type.Name, name);
                return Error(syntax, record);
        }
    }

    /// <summary>
    /// LANG-624 <c>inherited Name(args)</c>: the nearest ancestor's method, called statically on <c>Self</c>. Bare <c>inherited</c>
    /// (<paramref name="bare"/>, statements only) calls the ancestor method of the current method's name with the current
    /// parameters, or does nothing when there is none (null method, no error).
    /// </summary>
    private MethodCall BindInherited(SyntaxNode call, InheritedExpressionSyntax inherited, ArgumentListSyntax? argumentList, bool bare)
    {
        if (CurrentMethod is not { } current)
        {
            _diagnostics.Report(call, "E2133");                                      // LANG-624
            return new MethodCall(null, [], null);
        }

        if (inherited.Name is null && current.IsClassConstructorOrDestructor)
        {
            // LANG-788 c (C-374): nothing to call, the ancestor's class constructor runs by itself (LANG-789)
            _diagnostics.Report(call, "E2668", current.MethodKind == MethodKind.ClassConstructor ? "constructor" : "destructor");
            return new MethodCall(null, [], null);
        }

        if (bare && current.MessageId is { } id)
        {
            return InheritedMessage(call, current, id);
        }

        var name = inherited.Name is { Identifier: var token } ? token.Value as string ?? token.Text : current.Name;
        var candidates = (current.Owner as ClassTypeSymbol)?.Parent is { } parent ? MemberCandidates(parent, name) : [];
        if (bare)
        {
            var match = candidates.OfType<MethodSymbol>().FirstOrDefault(m => m.MethodKind == current.MethodKind
                && m.Parameters.Length == current.Parameters.Length
                && m.Parameters.Zip(current.Parameters).All(p => p.First.Mode == p.Second.Mode && ReferenceEquals(p.First.Type, p.Second.Type)));
            if (match is null)
            {
                return new MethodCall(null, [], null, IsNothing: true);                 // nothing to call (LANG-624)
            }

            var receiver = Receiver(call, match, ReceiverKind.Inherited, null, null);
            ImmutableArray<BoundExpression> arguments = [.. current.Parameters.Select(p =>
            {
                NoteReference(p);
                return (BoundExpression)new BoundParameterExpression(call, p);
            })];
            return new MethodCall(receiver is null ? null : match, arguments, receiver);
        }

        if (candidates.IsEmpty)
        {
            _diagnostics.ReportUndeclared(inherited.Name!, name);
            return new MethodCall(null, [], null);
        }

        return BindMethodCall(call, candidates, ReceiverKind.Inherited, null, null, argumentList);
    }

    /// <summary>
    /// LANG-622 / CMP-370 (C-113): bare <c>inherited</c> in a <c>message</c> handler calls the nearest ancestor handler for the same id
    /// statically, else virtual <c>DefaultHandler</c> (ABI-073 slot 9), passing the handler's own message parameter.
    /// </summary>
    private MethodCall InheritedMessage(SyntaxNode call, MethodSymbol current, int id)
    {
        var message = current.Parameters[0];
        NoteReference(message);
        ImmutableArray<BoundExpression> arguments = [new BoundParameterExpression(call, message)];
        for (var type = current.ContainingType.Parent; type is not null; type = type.Parent)
        {
            if (type.Methods.FirstOrDefault(m => m.MessageId == id) is { } handler)
            {
                return new MethodCall(handler, arguments, Receiver(call, handler, ReceiverKind.Inherited, null, null));
            }
        }

        var fallback = current.ContainingType.VirtualSlots.Length > 9 ? current.ContainingType.VirtualSlots[9] : null;
        if (fallback is null || !IdentifierComparer.Instance.Equals(fallback.Name, "DefaultHandler"))
        {
            return new MethodCall(null, [], null, IsNothing: true);                     // no TObject.DefaultHandler (stripped System)
        }

        return new MethodCall(fallback, arguments, Receiver(call, fallback, ReceiverKind.Implicit, null, null));
    }

    /// <summary>
    /// A statement calling a method (<c>M(x)</c> inside a method, <c>o.M(x)</c>, <c>TFoo.Create</c>, <c>inherited …</c>) or, through
    /// a member access, a procedural field; null when the statement does not start with such a designator (the caller binds it).
    /// </summary>
    public BoundStatement? BindMethodCallStatement(StatementSyntax statement, ExpressionSyntax expression)
    {
        var invocation = expression as InvocationExpressionSyntax;
        var callee = invocation?.Expression ?? expression;
        var argumentList = invocation?.ArgumentList;
        MethodCall? call;
        switch (callee)
        {
            case SimpleNameSyntax when ImplicitGenericMethod(expression, callee, argumentList) is { } genericCall:
                call = genericCall;                                                         // C-222
                break;
            case InheritedExpressionSyntax inherited:
                call = BindInherited(expression, inherited, argumentList, bare: inherited.Name is null && invocation is null);
                if (call.Value.IsNothing)
                {
                    return new BoundBlock(statement, []);                                   // bare inherited without ancestor method
                }

                break;
            case var _ when _scope.Unqualified(callee) is IdentifierNameSyntax { Identifier: { IsMissing: false } token } name:
                if (_scope.For(name).Lookup(token.Value as string ?? token.Text) is not MethodSymbol)
                {
                    return null;
                }

                call = ImplicitMethod(expression, name, argumentList);
                break;
            case MemberAccessExpressionSyntax member:
            {
                var value = BindMemberCall(member, expression, argumentList, out call);
                if (call is null)
                {
                    // a D.Create(…) value (LANG-271) has consumed its argument list: E2060 like any other value
                    return value!.HasErrors ? new BoundErrorStatement(statement, value)
                        : MemberValueStatement(statement, expression, value, value is BoundArrayConstructor ? null : argumentList);
                }

                break;
            }

            default:
                return null;
        }

        return call.Value.Method is { } method
            ? new BoundCallStatement(statement, method, call.Value.Arguments) { Receiver = call.Value.Receiver }
            : new BoundErrorStatement(statement);
    }

    /// <summary>A member that is no method in statement position: a call through a procedural field (LANG-484) or E2060 / E2900.</summary>
    private BoundStatement MemberValueStatement(StatementSyntax statement, ExpressionSyntax expression, BoundExpression value, ArgumentListSyntax? argumentList)
    {
        if (SignatureOf(value.Type) is { } type)
        {
            var (signature, arguments) = BindCall(expression, [type], argumentList);
            return signature is null ? new BoundErrorStatement(statement) : new BoundCallStatement(statement, signature, arguments, value);
        }

        if (argumentList is not null)
        {
            Unsupported(expression, "routine call");
            return new BoundErrorStatement(statement);
        }

        _diagnostics.Report(expression, "E2060", value.Type.Name);                       // LANG-352
        return new BoundExpressionStatement(statement, value);
    }
}
