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

    public Scope? InterfaceScope => Declarations.InterfaceScope;

    public Scope Scope => Declarations.Scope;
}

/// <summary>
/// One bound routine body (CMP-114). <paramref name="Scope"/> is the routine's own scope (<c>Result</c>, parameters,
/// locals); <paramref name="NestedRoutines"/> are the routines declared in its declaration part.
/// </summary>
internal sealed record BoundRoutineBody(
    RoutineSymbol Routine,
    Scope Scope,
    BoundBlock Body,
    ImmutableArray<RoutineSymbol> NestedRoutines,
    ImmutableArray<Diagnostic> Diagnostics);

/// <summary>Binds a compilation unit: declarations (CMP-113), then the routine and unit-level bodies (CMP-114).</summary>
internal static class Binder
{
    public static BindingResult BindCompilationUnit(SyntaxTree tree, BuiltInTypes types)
    {
        var diagnostics = new DiagnosticBag();
        var declarations = DeclarationBinder.BindCompilationUnit(tree, types, diagnostics);

        // routine bodies, each with its own E2003 set (CMP-507); nested routines after their enclosing routine
        var routines = ImmutableArray.CreateBuilder<BoundRoutineBody>();
        var pending = new Queue<RoutineSymbol>(declarations.Unit.InterfaceMembers.Concat(declarations.Unit.ImplementationMembers)
            .OfType<RoutineSymbol>().Where(r => r.Definition != null)
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
        return new BindingResult(types, declarations, bodies.ToImmutable(), routines.ToImmutable(), [.. all]);
    }

    /// <summary>Binds one routine body on its own (CMP-114), e.g. for the language service.</summary>
    public static BoundRoutineBody BindRoutineBody(BindingResult unit, RoutineSymbol routine) => BindRoutineBody(unit.Types, routine);

    private static BoundRoutineBody BindRoutineBody(BuiltInTypes types, RoutineSymbol routine)
    {
        var definition = routine.Definition ?? throw new InvalidOperationException($"Routine '{routine.Name}' has no body.");
        var diagnostics = new DiagnosticBag();
        var scope = new Scope(ScopeKind.Routine, routine.DefinitionScope, routine);
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
        var body = new BodyBinder(types, scope, diagnostics, routine).BindBlock(block.Body, block.Body.Statements);
        return new BoundRoutineBody(routine, scope, body, [.. locals.OfType<RoutineSymbol>()], diagnostics.ToImmutable());
    }
}
