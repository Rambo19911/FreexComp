using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>Declarations and bodies of one compilation unit.</summary>
/// <param name="Bodies">
/// The program block, or a unit's initialization then finalization section (those present), in source order.
/// </param>
/// <param name="Routines">Every routine body: definition order, each nested routine after its enclosing routine.</param>
/// <param name="Diagnostics">All binder diagnostics of the unit, by file and position.</param>
internal sealed record BindingResult(
    BuiltInTypes Types,
    DeclarationBindingResult Declarations,
    ImmutableArray<BoundBlock> Bodies,
    ImmutableArray<BoundRoutineBody> Routines,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public UnitSymbol Unit => Declarations.Unit;

    /// <summary>
    /// The unit <c>System</c> this unit was bound against (LANG-511): the implicitly used one, the unit itself when it is
    /// <c>System</c>, or null when it was bound without one (white-box tests).
    /// </summary>
    public UnitSymbol? System { get; init; }

    /// <summary>The bound tree (null for units read from a precompiled file).</summary>
    public SyntaxTree? Tree { get; init; }

    public Scope? InterfaceScope => Declarations.InterfaceScope;

    public Scope Scope => Declarations.Scope;

    /// <summary>
    /// CMP-345: the variables, parameters and <c>Result</c> of <paramref name="routine"/> referenced by any routine nested in
    /// it (at any depth), in the order of <paramref name="routine"/>'s scope (<c>Result</c>, parameters, locals).
    /// </summary>
    public ImmutableArray<Symbol> GetCapturedVariables(RoutineSymbol routine)
    {
        var captured = Routines.SelectMany(r => r.CapturedVariables).Where(s => ReferenceEquals(s.ContainingSymbol, routine)).ToHashSet();
        // an open array parameter is followed by its High companion, which is in no scope (C-106)
        return captured.Count == 0 ? [] : [.. Routines.First(r => ReferenceEquals(r.Routine, routine)).Scope.Symbols.Where(captured.Contains)
            .SelectMany(s => s is ParameterSymbol { HighParameter: { } high } && captured.Contains(high) ? [s, high] : new[] { s })];
    }
}

/// <summary>
/// One bound routine body (CMP-114). <paramref name="Scope"/> is the routine's own scope (<c>Result</c>, parameters,
/// locals); <paramref name="NestedRoutines"/> are the routines declared in its declaration part.
/// <paramref name="CapturedVariables"/> are the variables, parameters and <c>Result</c>s of enclosing routines this body
/// references (first-reference order; references from deeper nested bodies are listed there).
/// </summary>
internal sealed record BoundRoutineBody(
    RoutineSymbol Routine,
    Scope Scope,
    BoundBlock Body,
    ImmutableArray<RoutineSymbol> NestedRoutines,
    ImmutableArray<Symbol> CapturedVariables,
    ImmutableArray<Diagnostic> Diagnostics)
{
    /// <summary>Variables of this body whose address is taken (<c>@x</c>, <c>Addr(x)</c>, also of a part of <c>x</c>).</summary>
    public ImmutableHashSet<Symbol> AddressTaken { get; init; } = [];
}

/// <summary>Binds a compilation unit: declarations (CMP-113), then the routine and unit-level bodies (CMP-114).</summary>
internal static class Binder
{
    /// <param name="system">
    /// The bound unit <c>System</c> (same <paramref name="types"/>), used implicitly (LANG-511): its interface scope sits
    /// between the built-ins and this unit's scope (LANG-310). Null for <c>System</c> itself and for white-box tests.
    /// Other uses entries are not resolved by this entry point.
    /// </param>
    public static BindingResult BindCompilationUnit(SyntaxTree tree, BuiltInTypes types, BindingResult? system = null)
    {
        // white-box entry point: uses clauses are not resolved here (the compilation's UnitBinder does that, C-069)
        var diagnostics = new DiagnosticBag();
        var outer = SystemScope(types, system);
        var interfacePart = DeclarationBinder.BindInterface(tree, types, diagnostics, outer);
        return Complete(tree, interfacePart, types, diagnostics, outer, system?.Unit);
    }

    /// <summary>
    /// The root of every unit's uses chain (LANG-310, 511): the built-ins, then the interface of <c>System</c> (qualifier
    /// <c>System</c>) when bound.
    /// </summary>
    public static Scope SystemScope(BuiltInTypes types, BindingResult? system) =>
        system?.InterfaceScope is { } systemInterface ? Scope.Uses(systemInterface, types.Scope, ["System"]) : types.Scope;

    /// <summary>
    /// The second phase of binding a compilation unit whose interface part is bound (<see cref="DeclarationBinder.BindInterface"/>):
    /// the implementation declarations in the chain <paramref name="implementationUses"/>, then the routine bodies and the
    /// unit-level bodies. <paramref name="diagnostics"/> is the bag of the interface phase (one E2003 set per unit level).
    /// </summary>
    public static BindingResult Complete(SyntaxTree tree, DeclarationBindingResult interfacePart, BuiltInTypes types, DiagnosticBag diagnostics,
        Scope implementationUses, UnitSymbol? system)
    {
        var declarations = DeclarationBinder.BindImplementation(tree, interfacePart, types, diagnostics, implementationUses);

        // routine bodies, each with its own E2003 set (CMP-507); nested routines after their enclosing routine
        var routines = ImmutableArray.CreateBuilder<BoundRoutineBody>();
        // methods of the unit's classes (C-081) are bound with the routines, in definition order
        var pending = new Queue<RoutineSymbol>(declarations.Unit.InterfaceMembers.Concat(declarations.Unit.ImplementationMembers)
            .OfType<RoutineSymbol>()
            .Concat(DeclarationBinder.ClassesOf(declarations.Unit).SelectMany(c => c.Methods))
            .Where(r => r.Definition != null)
            .OrderBy(r => r.Definition!.Span.Start));
        while (pending.Count > 0)
        {
            var routine = BindRoutineBody(types, pending.Dequeue());
            routines.Add(routine);
            foreach (var nested in routine.NestedRoutines.Where(r => r.Definition != null))
            {
                pending.Enqueue(nested);
            }
        }

        var body = new BodyBinder(types, declarations.Scope, diagnostics);
        var bodies = ImmutableArray.CreateBuilder<BoundBlock>();
        switch (tree.Root)
        {
            case ProgramSyntax program:
                bodies.Add(body.BindBlock(program.Block.Body, program.Block.Body.Statements));
                break;
            case UnitSyntax unit:
                if (unit.InitializationSection is { } initialization)
                {
                    bodies.Add(body.BindBlock(initialization, initialization.Statements));
                }

                if (unit.FinalizationSection is { } finalization)
                {
                    bodies.Add(body.BindBlock(finalization, finalization.Statements));
                }

                break;
        }

        var all = diagnostics.ToImmutable().Concat(routines.SelectMany(r => r.Diagnostics))
            .OrderBy(d => d.Location.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Location.Span.Start);
        var systemUnit = system
            ?? (declarations.Unit is { IsProgram: false } own && string.Equals(own.Name, "System", StringComparison.OrdinalIgnoreCase) ? own : null);
        return new BindingResult(types, declarations, bodies.ToImmutable(), routines.ToImmutable(), [.. all]) { System = systemUnit, Tree = tree };
    }

    /// <summary>Binds one routine body on its own (CMP-114), e.g. for the language service.</summary>
    public static BoundRoutineBody BindRoutineBody(BindingResult unit, RoutineSymbol routine) => BindRoutineBody(unit.Types, routine);

    private static BoundRoutineBody BindRoutineBody(BuiltInTypes types, RoutineSymbol routine)
    {
        var definition = routine.Definition ?? throw new InvalidOperationException($"Routine '{routine.Name}' has no body.");
        var diagnostics = new DiagnosticBag();
        // a routine sees the enclosing scope's declarations up to its own definition (LANG-402, C-172); for a unit's
        // implementation routine that limits the implementation scope only (the interface stays visible as its parent)
        var outer = routine.DefinitionScope!;
        Scope scope;
        if (routine is MethodSymbol method)
        {
            // LANG-621: the members of the class and its ancestors are in scope (one scope per class, the class innermost); the
            // limit of LANG-402 applies to the definition scope. Self comes first; parameters and locals hide members.
            var members = outer;
            int? visible = routine.DefinitionScopeVisibleCount;
            foreach (var type in method.ContainingType.SelfAndAncestors().Reverse())
            {
                members = MemberScope(type, members, visible);
                visible = null;
            }

            scope = new Scope(ScopeKind.Routine, members, routine);
            if (method.SelfParameter is { } self)
            {
                scope.TryDeclare(self, out _);
            }
        }
        else
        {
            scope = new Scope(ScopeKind.Routine, outer, routine, routine.DefinitionScopeVisibleCount);
        }

        if (routine.ResultVariable is { } result)
        {
            scope.TryDeclare(result, out _);
        }

        foreach (var parameter in routine.Parameters)
        {
            scope.TryDeclare(parameter, out _);                                 // duplicates were reported with the heading
        }

        var block = definition.Block!;
        var locals = new DeclarationBinder(types, scope, routine, diagnostics).BindDeclarations(block.Declarations);
        var binder = new BodyBinder(types, scope, diagnostics, routine);
        binder.AddReadOnlyVariables(EnclosingForVariables(outer));
        var body = binder.BindBlock(block.Body, block.Body.Statements);
        if (routine is MethodSymbol { MethodKind: MethodKind.Destructor, ContainingType.Parent: not null } destructor
            && IdentifierComparer.Instance.Equals(destructor.Name, "Destroy")
            && !block.Body.DescendantTokens().Any(t => t.Kind == SyntaxKind.InheritedKeyword))
        {
            // LANG-634: a Destroy of a class with an ancestor should call 'inherited' (a root class has nothing to call)
            diagnostics.Report(definition.Heading.Name, "W1062", destructor.ContainingType.Name + "." + destructor.Name);
        }

        return new BoundRoutineBody(routine, scope, body, [.. locals.OfType<RoutineSymbol>()], [.. binder.CapturedVariables], diagnostics.ToImmutable())
        {
            AddressTaken = [.. binder.AddressTakenVariables],
        };
    }

    /// <summary>A fresh scope with the own members of <paramref name="type"/> (overloaded methods as one overload set).</summary>
    private static Scope MemberScope(ClassTypeSymbol type, Scope parent, int? parentVisibleCount)
    {
        var scope = new Scope(ScopeKind.TypeMember, parent, type, parentVisibleCount);
        foreach (var member in type.Members)
        {
            if (!scope.TryDeclare(member, out var existing) && member is RoutineSymbol routine && existing is RoutineSymbol)
            {
                scope.AddOverload(routine);
            }
        }

        return scope;
    }

    /// <summary>
    /// LANG-452: the <c>for</c> control variables (locals and value parameters) of the routines enclosing a nested routine
    /// whose scope chain starts at <paramref name="scope"/>.
    /// </summary>
    private static IEnumerable<Symbol> EnclosingForVariables(Scope scope)
    {
        for (var s = scope; s is { Kind: ScopeKind.Routine, Owner: RoutineSymbol { Definition.Block: { } block } owner }; s = s.Parent)
        {
            foreach (var loop in block.Body.DescendantNodes().OfType<ForStatementSyntax>())
            {
                if (loop.Variable is IdentifierNameSyntax { Identifier: { IsMissing: false } token }
                    && s.LookupLocal(token.Value as string ?? token.Text) is (VariableSymbol or ParameterSymbol) and var symbol
                    && ReferenceEquals(symbol.ContainingSymbol, owner))
                {
                    yield return symbol;
                }
            }
        }
    }
}
