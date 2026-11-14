using System.Collections.Immutable;
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
            for (var routine = _routine; routine != null; routine = routine.ContainingSymbol as RoutineSymbol)
            {
                if (routine is MethodSymbol method)
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
        var self = method.SelfParameter!;
        NoteReference(self);
        return new BoundParameterExpression(syntax, self);
    }

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
    private bool CheckAccess(SyntaxNode at, Symbol member, ClassTypeSymbol owner, TypeSymbol? receiver)
    {
        var visibility = member.Visibility;
        if (visibility is Visibility.Public or Visibility.Published)
        {
            return true;
        }

        member.IsReferenced = true;                                                     // H2003 (C-096)
        var current = CurrentMethod?.ContainingType;
        var sameUnit = owner.ContainingSymbol is UnitSymbol unit && ReferenceEquals(unit, CurrentUnit);
        var own = ReferenceEquals(current, owner);
        var descendant = current is not null && !own && current.SelfAndAncestors().Contains(owner)
            && receiver is ClassTypeSymbol r && r.SelfAndAncestors().Contains(current);
        var allowed = visibility switch
        {
            Visibility.Private => sameUnit,
            Visibility.StrictPrivate => own,
            Visibility.Protected => sameUnit || own || descendant,
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
            _diagnostics.Report(at, "E2610", text, member.Name, owner.Name);
        }

        return allowed;
    }

    /// <summary>A <c>class var</c> (LANG-615, CMP-370): the class's global, after the visibility check against <paramref name="receiver"/>.</summary>
    private BoundExpression ClassVariable(SyntaxNode syntax, SyntaxNode at, VariableSymbol classVar, TypeSymbol? receiver) =>
        CheckAccess(at, classVar, (ClassTypeSymbol)classVar.ContainingSymbol!, receiver) ? new BoundVariableExpression(syntax, classVar) : Error(syntax);

    /// <summary>A field named inside a method (LANG-621): <c>Self.F</c>.</summary>
    private BoundExpression ImplicitField(IdentifierNameSyntax name, FieldSymbol field)
    {
        if (CurrentMethod is not { SelfParameter: not null, IsClassMethod: false } method)
        {
            _diagnostics.Report(name, "E2608", field.Name);
            return Error(name);
        }

        if (field.ContainingSymbol is ClassTypeSymbol owner && !CheckAccess(name, field, owner, method.ContainingType))
        {
            return Error(name);
        }

        return new BoundFieldAccessExpression(name, SelfOf(method, name), field);
    }

    /// <summary>
    /// LANG-441 over the inheritance chain: the methods named <paramref name="name"/> of the nearest class declaring that name,
    /// and of further ancestors while all of them carry <c>overload</c>; empty when the nearest declaration is no method.
    /// </summary>
    private static ImmutableArray<RoutineSymbol> MemberCandidates(ClassTypeSymbol type, string name)
    {
        var candidates = ImmutableArray.CreateBuilder<RoutineSymbol>();
        foreach (var owner in type.SelfAndAncestors())
        {
            if (owner.Members.Any(m => m is not MethodSymbol && IdentifierComparer.Instance.Equals(m.Name, name)))
            {
                break;                                                          // a field, class var or property hides the methods
            }

            var methods = owner.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, name)).ToList();
            // an ancestor's method that a nearer one overrides or hides with the same parameters is not a second candidate
            candidates.AddRange(methods.Where(m => !candidates.Any(c => SameParameters(c, m))));
            if (methods.Count > 0 && methods.Any(m => !m.IsOverload))
            {
                break;
            }
        }

        return candidates.ToImmutable();
    }

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
    private MethodCall BindMethodCall(SyntaxNode call, ImmutableArray<RoutineSymbol> candidates, ReceiverKind kind, BoundExpression? instance,
        ClassTypeSymbol? className, ArgumentListSyntax? argumentList)
    {
        var (routine, arguments) = BindCall(call, candidates, argumentList);
        if (routine is not MethodSymbol method)
        {
            return new MethodCall(null, arguments, null);
        }

        var receiverType = kind switch
        {
            ReceiverKind.Instance => instance!.Type is ClassRefTypeSymbol reference ? reference.Class : instance.Type,
            ReceiverKind.ClassName => className,
            _ => CurrentMethod?.ContainingType,
        };
        if (method.ContainingInterface is null && !CheckAccess(call, method, method.ContainingType, receiverType))
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
        TypeSymbol? target = null;
        if (_scope.Unqualified(Unparenthesized(binary.Right)) is IdentifierNameSyntax { Identifier: { IsMissing: false } token } name)
        {
            target = _scope.For(name).Lookup(token.Value as string ?? token.Text) as TypeSymbol;
            if (target is null && _scope.For(name).Lookup(token.Value as string ?? token.Text) is null)
            {
                _diagnostics.ReportUndeclared(name, token.Value as string ?? token.Text);
                return Error(binary, operand);
            }
        }

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
                return new BoundReceiver(new BoundClassReferenceExpression(call, className!, className!.Reference), IsVirtual: false, Flag: null);
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

    private BoundReceiver? Receiver(SyntaxNode call, MethodSymbol method, ReceiverKind kind, BoundExpression? instance, ClassTypeSymbol? className)
    {
        if (method.IsClassMethod)
        {
            return ClassMethodReceiver(call, method, kind, instance, className);
        }

        if (kind == ReceiverKind.Instance && instance!.Type is ClassRefTypeSymbol)
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
                return new BoundReceiver(new BoundClassReferenceExpression(call, className!, className!.Reference), IsVirtual: false, Flag: 1);
            case ReceiverKind.ClassName:
                _diagnostics.Report(call, "E2607");
                return null;
            case ReceiverKind.Instance:
                return Dispatched(method, instance!, inherited: false);
        }

        var current = CurrentMethod;
        if (current is not { SelfParameter: not null, IsClassMethod: false })
        {
            _diagnostics.Report(call, "E2608", method.Name);
            return null;
        }

        if (kind == ReceiverKind.Inherited && method.IsAbstract)
        {
            _diagnostics.Report(call, "E2626", method.Name);                         // LANG-624
            return null;
        }

        return Dispatched(method, SelfOf(current, call), inherited: kind == ReceiverKind.Inherited);
    }

    /// <summary>
    /// CMP-367: a constructor on an instance runs the body only (Alloc = 0, static); a destructor passes Dealloc = 1 (0 for
    /// <c>inherited</c>); virtual methods dispatch through the VMT except with <c>inherited</c> (LANG-624).
    /// </summary>
    private static BoundReceiver Dispatched(MethodSymbol method, BoundExpression self, bool inherited) => method.MethodKind switch
    {
        MethodKind.Constructor => new BoundReceiver(self, IsVirtual: false, Flag: 0),
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

    private MethodCall ImplicitMethod(SyntaxNode call, IdentifierNameSyntax name, ArgumentListSyntax? argumentList)
    {
        var text = name.Identifier.Value as string ?? name.Identifier.Text;
        if (_scope.For(name).Lookup(text) is { } symbol && WithOf(text, symbol) is { Type: ClassTypeSymbol type } with)
        {
            return BindMethodCall(call, MemberCandidates(type, text), ReceiverKind.Instance, WithReceiver(name, with), null, argumentList);
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
            if (candidate is MethodSymbol { ContainingInterface: null } method
                && kept.Any(k => k is MethodSymbol { ContainingInterface: null } nearer && !ReferenceEquals(nearer.ContainingType, method.ContainingType)
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
                return field.ContainingSymbol is ClassTypeSymbol owner && !CheckAccess(name, field, owner, type) ? Error(name) : new BoundFieldAccessExpression(name, receiver, field);
            case FieldSymbol field:
                return new BoundFieldAccessExpression(name, receiver, field);
            case VariableSymbol classVar:
                return ClassVariable(name, name, classVar, with.Type);
            case PropertySymbol property:
                return CheckAccess(name, property, property.ContainingType, with.Type) ? PropertyAccess(name, property, property.IsClassProperty ? null : receiver) : Error(name);
            case MethodSymbol:
                return ImplicitMethodCall(name, name, argumentList: null);
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
            return Error(syntax);
        }

        var name = syntax.Name.Identifier.Value as string ?? syntax.Name.Identifier.Text;
        if (_scope.Unqualified(Unparenthesized(syntax.Expression)) is IdentifierNameSyntax { Identifier: { IsMissing: false } qualifier } qualifierName
            && _scope.For(qualifierName).Lookup(qualifier.Value as string ?? qualifier.Text) is ClassTypeSymbol className)
        {
            var candidates = MemberCandidates(className, name);
            switch (className.GetMembers(name).FirstOrDefault())
            {
                case PropertySymbol { IsClassProperty: true } property:                 // LANG-647
                    return CheckAccess(syntax.Name, property, property.ContainingType, className) ? PropertyAccess(syntax, property, self: null) : Error(syntax);
                case VariableSymbol classVar:                                           // LANG-615
                    return ClassVariable(syntax, syntax.Name, classVar, className);
            }

            if (candidates.IsEmpty)
            {
                _diagnostics.Report(syntax.Name, className.GetMembers(name).Any() ? "E2607" : "E2005", className.Name, name);
                return Error(syntax);
            }

            methodCall = BindMethodCall(call, candidates, ReceiverKind.ClassName, null, className, argumentList);
            return null;
        }

        var receiver = BindExpression(syntax.Expression);
        if (receiver.HasErrors)
        {
            return Error(syntax, receiver);
        }

        if (receiver.Type is ClassTypeSymbol type)
        {
            var candidates = MemberCandidates(type, name);
            if (!candidates.IsEmpty)
            {
                methodCall = BindMethodCall(call, candidates, ReceiverKind.Instance, receiver, null, argumentList);
                return null;
            }

            switch (type.GetMembers(name).FirstOrDefault())
            {
                case FieldSymbol field:
                    if (field.ContainingSymbol is ClassTypeSymbol owner && !CheckAccess(syntax.Name, field, owner, type))
                    {
                        return Error(syntax, receiver);
                    }

                    return new BoundFieldAccessExpression(syntax, receiver, field);
                case VariableSymbol classVar:
                    return ClassVariable(syntax, syntax.Name, classVar, type);
                case PropertySymbol property:
                    if (!CheckAccess(syntax.Name, property, property.ContainingType, type))
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
                    return CheckAccess(syntax.Name, property, property.ContainingType, target) ? PropertyAccess(syntax, property, self: null) : Error(syntax, receiver);
                case VariableSymbol classVar:
                    return ClassVariable(syntax, syntax.Name, classVar, target);
            }

            var candidates = MemberCandidates(target, name);
            if (candidates.IsEmpty)
            {
                _diagnostics.Report(syntax.Name, target.GetMembers(name).Any() ? "E2607" : "E2005", target.Name, name);
                return Error(syntax, receiver);
            }

            methodCall = BindMethodCall(call, candidates, ReceiverKind.Instance, receiver, null, argumentList);
            return null;
        }

        return RecordMember(syntax, receiver);
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

        if (bare && current.MessageId is { } id)
        {
            return InheritedMessage(call, current, id);
        }

        var name = inherited.Name is { Identifier: var token } ? token.Value as string ?? token.Text : current.Name;
        var candidates = current.ContainingType.Parent is { } parent ? MemberCandidates(parent, name) : [];
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
                    return value!.HasErrors ? new BoundErrorStatement(statement) : MemberValueStatement(statement, expression, value, argumentList);
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
