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
            if (owner.Fields.Any(f => IdentifierComparer.Instance.Equals(f.Name, name)))
            {
                break;
            }

            var methods = owner.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, name)).ToList();
            candidates.AddRange(methods);
            if (methods.Count > 0 && methods.Any(m => !m.IsOverload))
            {
                break;
            }
        }

        return candidates.ToImmutable();
    }

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
            ReceiverKind.Instance => instance!.Type,
            ReceiverKind.ClassName => className,
            _ => CurrentMethod?.ContainingType,
        };
        if (!CheckAccess(call, method, method.ContainingType, receiverType))
        {
            return new MethodCall(null, arguments, null);
        }

        var receiver = Receiver(call, method, kind, instance, className);
        return new MethodCall(receiver is null ? null : method, arguments, receiver);
    }

    private BoundReceiver? Receiver(SyntaxNode call, MethodSymbol method, ReceiverKind kind, BoundExpression? instance, ClassTypeSymbol? className)
    {
        if (method.IsClassMethod)
        {
            Unsupported(call, "class method call");                                     // LANG-653, C-086
            return null;
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
                return new BoundReceiver(new BoundClassReferenceExpression(call, className!, _types.Pointer), IsVirtual: false, Flag: 1);
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
            _ => null,
        } : null;
        return new BoundCallExpression(call, method.Method, method.Arguments, type: type) { Receiver = method.Receiver };
    }

    /// <summary>A method named without qualifier inside a method: <c>Self.M(args)</c>.</summary>
    private BoundExpression ImplicitMethodCall(SyntaxNode call, IdentifierNameSyntax name, ArgumentListSyntax? argumentList) =>
        MethodCallExpression(call, BindMethodCall(call, _scope.For(name).LookupCandidates(name.Identifier.Value as string ?? name.Identifier.Text),
            ReceiverKind.Implicit, null, null, argumentList));

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

            if (type.GetMembers(name).FirstOrDefault() is FieldSymbol field)
            {
                if (field.ContainingSymbol is ClassTypeSymbol owner && !CheckAccess(syntax.Name, field, owner, type))
                {
                    return Error(syntax, receiver);
                }

                return new BoundFieldAccessExpression(syntax, receiver, field);
            }

            _diagnostics.Report(syntax.Name, "E2005", type.Name, name);
            return Error(syntax, receiver);
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
            Unsupported(call, "inherited outside a method");
            return new MethodCall(null, [], null);
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

                call = BindMethodCall(expression, _scope.For(name).LookupCandidates(token.Value as string ?? token.Text), ReceiverKind.Implicit, null, null, argumentList);
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
        if (value.Type is ProceduralTypeSymbol type)
        {
            var (signature, arguments) = BindCall(expression, [type.Signature], argumentList);
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
