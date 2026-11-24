using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Inline variables and constants (LANG-385), for var (LANG-386), the for…in enumerator pattern (LANG-362) — C-125.
internal sealed partial class BodyBinder
{
    /// <summary>The inline variables declared so far in this body (they may be <c>for</c> control variables at unit level too).</summary>
    private readonly HashSet<VariableSymbol> _inlineVariables = [];

    /// <summary>The routine, or the unit of a unit-level body: the container of inline declarations.</summary>
    private Symbol Container
    {
        get
        {
            if (_routine is not null)
            {
                return _routine;
            }

            for (var scope = _scope; scope is not null; scope = scope.Parent)
            {
                if (scope.Owner is UnitSymbol unit)
                {
                    return unit;
                }
            }

            throw new InvalidOperationException("A body without unit scope.");
        }
    }

    private DeclarationBinder Declarations() => new(_types, _scope, Container, _diagnostics);

    /// <summary>
    /// LANG-385: E2004 when <paramref name="name"/> is already declared in an enclosing block of this body or in the routine's own
    /// scope (parameters, <c>Result</c>, locals).
    /// </summary>
    private bool CheckInlineName(SyntaxToken identifier, string name)
    {
        for (var scope = _scope; scope is { Kind: ScopeKind.Block or ScopeKind.Routine }; scope = scope.Parent)
        {
            if (scope.LookupLocal(name) is { } existing)
            {
                _diagnostics.ReportRedeclared(identifier, name, existing);
                return false;
            }

            if (scope.Kind == ScopeKind.Routine)
            {
                break;
            }
        }

        return true;
    }

    /// <summary>
    /// LANG-385: the type of <c>var x := e</c> — an integer constant is <c>Integer</c> (<c>Int64</c>/<c>UInt64</c> when it does not fit),
    /// a character constant <c>Char</c>, a string constant <c>string</c>, a real constant <c>Double</c>; <c>nil</c> and set/array
    /// constructors are E2075. Null after an error.
    /// </summary>
    private TypeSymbol? InferredType(BoundExpression value, SyntaxNode at, string name)
    {
        if (value.HasErrors)
        {
            return null;
        }

        if (value is BoundSetConstructor or BoundArrayConstructor || ReferenceEquals(value.Type, _types.Nil))
        {
            _diagnostics.Report(at, "E2075", name);
            return null;
        }

        if (value.ConstantValue is not { } constant)
        {
            return value.Type;
        }

        return constant.Type switch
        {
            IntegerTypeSymbol when constant.Value is BigInteger n => n >= int.MinValue && n <= int.MaxValue ? _types.Integer
                : n >= long.MinValue && n <= long.MaxValue ? _types.Int64 : _types.UInt64,
            CharTypeSymbol => _types.Char,
            StringTypeSymbol => _types.String,
            RealTypeSymbol => _types.Double,
            _ => value.Type,
        };
    }

    /// <summary>A new inline local in <paramref name="scope"/> (E2004 checked by the caller).</summary>
    private VariableSymbol InlineLocal(Scope scope, SyntaxToken identifier, string name, TypeSymbol type)
    {
        var local = new VariableSymbol(name, type, isGlobal: false, _routine, [DiagnosticBag.LocationOf(identifier)]);
        scope.TryDeclare(local, out _);
        _inlineVariables.Add(local);
        return local;
    }

    /// <summary>
    /// <c>var x: T;</c> / <c>var x: T := e;</c> / <c>var x := e;</c> / <c>const c [: T] = e;</c> (LANG-385): the locals, their
    /// initialisation and the block scope the rest of the statement list is bound in.
    /// </summary>
    private (ImmutableArray<VariableSymbol> Locals, ImmutableArray<BoundStatement> Initialization, Scope Scope) BindInlineDeclaration(StatementSyntax statement)
    {
        var scope = new Scope(ScopeKind.Block, _scope, _routine);
        var locals = ImmutableArray.CreateBuilder<VariableSymbol>();
        var initialization = ImmutableArray.CreateBuilder<BoundStatement>();
        switch (statement)
        {
            case InlineVarStatementSyntax syntax:
            {
                var type = syntax.Type is { } typeSyntax ? Declarations().BindLocalType(typeSyntax) : null;
                var value = syntax.Initializer is { } initializer
                    ? type is null ? _expressions.BindExpression(initializer) : _expressions.BindExpression(initializer, type)
                    : null;
                var first = syntax.Identifiers.Count > 0 ? syntax.Identifiers[0] : null;
                var firstName = first is null ? "" : first.Identifier.Value as string ?? first.Identifier.Text;
                if (type is null && first is not null)
                {
                    if (value is null)
                    {
                        _diagnostics.Report(first, "E2075", firstName);
                    }
                    else
                    {
                        type = InferredType(value, first, firstName);
                    }
                }

                if (value is not null && syntax.Identifiers.Count > 1)
                {
                    _diagnostics.Report(syntax.Initializer!, "E2900", "initialised inline variable list");
                    value = null;
                }

                foreach (var name in syntax.Identifiers.Where(i => !i.Identifier.IsMissing))
                {
                    var text = name.Identifier.Value as string ?? name.Identifier.Text;
                    if (!CheckInlineName(name.Identifier, text))
                    {
                        continue;
                    }

                    if (scope.LookupLocal(text) is { } duplicate)
                    {
                        _diagnostics.ReportRedeclared(name.Identifier, text, duplicate);
                        continue;
                    }

                    var local = InlineLocal(scope, name.Identifier, text, type ?? _types.Error);
                    locals.Add(local);
                    if (value is not null && type is not null && type.TypeKind != TypeKind.Error)
                    {
                        initialization.Add(new BoundAssignmentStatement(syntax, new BoundVariableExpression(name, local),
                            _expressions.ConvertForAssignment(value, type, syntax.Initializer!)));
                    }
                }

                break;
            }

            case InlineConstStatementSyntax syntax when !syntax.Identifier.IsMissing:
            {
                var text = syntax.Identifier.Value as string ?? syntax.Identifier.Text;
                if (!CheckInlineName(syntax.Identifier, text))
                {
                    break;
                }

                if (syntax.Type is { } typeSyntax)
                {
                    // a typed inline constant: a read-only local initialised here (LANG-344 for routine-level constants)
                    var type = Declarations().BindLocalType(typeSyntax);
                    var value = _expressions.ConvertForAssignment(_expressions.BindExpression(syntax.Value, type), type, syntax.Value);
                    var local = new VariableSymbol(text, type, isGlobal: false, _routine, [DiagnosticBag.LocationOf(syntax.Identifier)]) { IsReadOnly = true };
                    scope.TryDeclare(local, out _);
                    locals.Add(local);
                    initialization.Add(new BoundAssignmentStatement(syntax, new BoundVariableExpression(syntax, local), value));
                    break;
                }

                var folded = new ConstantFolder(_types, _scope, _diagnostics).Fold(syntax.Value);
                scope.TryDeclare(new ConstantSymbol(text, folded?.Type ?? _types.Error, folded, isTyped: false, Container,
                    [DiagnosticBag.LocationOf(syntax.Identifier)]), out _);
                break;
            }
        }

        return (locals.ToImmutable(), initialization.ToImmutable(), scope);
    }

    /// <summary>LANG-386 <c>for var i [: T] := a to b do s</c>: <c>i</c> is a local of the loop (type inferred from <c>a</c>).</summary>
    private BoundStatement BindInlineFor(ForStatementSyntax syntax)
    {
        var start = _expressions.BindExpression(syntax.Start);
        var finish = _expressions.BindExpression(syntax.Finish);
        var identifier = syntax.Variable.Identifier;
        var name = identifier.Value as string ?? identifier.Text;
        var type = syntax.Type is { } typeSyntax ? Declarations().BindLocalType(typeSyntax) : InferredType(start, syntax.Variable, name);
        if (identifier.IsMissing || type is null || type.TypeKind == TypeKind.Error || !CheckInlineName(identifier, name))
        {
            return new BoundErrorStatement(syntax);
        }

        var scope = new Scope(ScopeKind.Block, _scope, _routine);
        var local = InlineLocal(scope, identifier, name, type);
        var loop = InScope(scope, () => BindForLoop(syntax, new BoundVariableExpression(syntax.Variable, local), start, finish));
        return new BoundLocalsStatement(syntax, [local], loop, LocalsScopeKind.Loop);
    }

    /// <summary>LANG-386 <c>for var x [: T] in e do s</c>: <c>x</c> is a local of the loop of the element type of <c>e</c>.</summary>
    private BoundStatement BindInlineForIn(ForInStatementSyntax syntax, BoundExpression collection)
    {
        if (collection.HasErrors)
        {
            return new BoundErrorStatement(syntax);
        }

        var identifier = syntax.Variable.Identifier;
        var name = identifier.Value as string ?? identifier.Text;
        var type = syntax.Type is { } typeSyntax ? Declarations().BindLocalType(typeSyntax) : ElementTypeOf(collection.Type);
        if (type is null)
        {
            _diagnostics.Report(syntax.Collection, "E2069", collection.Type.Name);
            return new BoundErrorStatement(syntax);
        }

        if (identifier.IsMissing || type.TypeKind == TypeKind.Error || !CheckInlineName(identifier, name))
        {
            return new BoundErrorStatement(syntax);
        }

        var scope = new Scope(ScopeKind.Block, _scope, _routine);
        var local = InlineLocal(scope, identifier, name, type);
        var loop = InScope(scope, () => BindForInLoop(syntax, new BoundVariableExpression(syntax.Variable, local), collection));
        return new BoundLocalsStatement(syntax, [local], loop, LocalsScopeKind.Loop);
    }

    /// <summary>The element type of a <c>for…in</c> collection (LANG-362), the enumerator's <c>Current</c> included; null when none.</summary>
    private TypeSymbol? ElementTypeOf(TypeSymbol collection) => collection switch
    {
        ArrayTypeSymbol array => array.ElementType,
        DynamicArrayTypeSymbol dynamic => dynamic.ElementType,
        OpenArrayTypeSymbol open => open.ElementType,
        StringTypeSymbol text => text.Variant == StringVariant.Unicode ? _types.Char : _types.AnsiChar,
        SetTypeSymbol { ElementType: { } member } => member,
        ClassTypeSymbol or RecordTypeSymbol or InterfaceTypeSymbol => _expressions.EnumeratorElementType(collection),
        _ => null,
    };

    /// <summary>
    /// LANG-362 enumerator pattern: <c>$en := e.GetEnumerator; [try] while $en.MoveNext do begin x := $en.Current; s end
    /// [finally $en.Free end]</c> (the <c>try</c> only for a class enumerator). E2069 when the pattern does not fit.
    /// </summary>
    private BoundStatement BindEnumeratorLoop(ForInStatementSyntax syntax, BoundExpression variable, Symbol control, BoundExpression collection)
    {
        var at = syntax.Collection;
        if (_expressions.EnumeratorCall(at, collection, "GetEnumerator") is not { Type: ClassTypeSymbol or RecordTypeSymbol or InterfaceTypeSymbol } getEnumerator)
        {
            _diagnostics.Report(at, "E2069", collection.Type.Name);
            return new BoundErrorStatement(syntax);
        }

        var enumerator = new VariableSymbol("$en" + ++_hiddenCount, getEnumerator.Type, isGlobal: false, _routine, []);
        var reference = new BoundVariableExpression(at, enumerator);
        var moveNext = _expressions.EnumeratorCall(at, reference, "MoveNext");
        var current = _expressions.EnumeratorCall(at, reference, "Current");
        if (moveNext is not { Type: BooleanTypeSymbol } || current is null)
        {
            _diagnostics.Report(at, "E2069", collection.Type.Name);
            return new BoundErrorStatement(syntax);
        }

        var store = new BoundAssignmentStatement(syntax.Variable, variable, _expressions.ConvertForAssignment(current, variable.Type, at));
        if (store.Value.HasErrors)
        {
            return new BoundErrorStatement(syntax);
        }

        var added = ForVariables.Add(control);
        _loopDepth++;
        var body = BindStatement(syntax.Statement);
        _loopDepth--;
        if (added)
        {
            ForVariables.Remove(control);
        }

        BoundStatement loop = new BoundWhileStatement(syntax, _expressions.ConvertForAssignment(moveNext, _types.Boolean, at), new BoundBlock(syntax, [store, body]));
        if (getEnumerator.Type is ClassTypeSymbol && _expressions.EnumeratorStatement(at, reference, "Free") is { } free)
        {
            loop = new BoundTryFinallyStatement(syntax, new BoundBlock(syntax, [loop]), new BoundBlock(syntax, [free]));
        }

        return new BoundLocalsStatement(syntax, [enumerator], new BoundBlock(syntax, [new BoundAssignmentStatement(at, reference, getEnumerator), loop]), LocalsScopeKind.Loop);
    }
}
