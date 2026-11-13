using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>Declarations and unit-level bodies of one compilation unit.</summary>
/// <param name="Bodies">
/// The program block, or a unit's initialization then finalization section (those present), in source order.
/// </param>
internal sealed record BindingResult(
    DeclarationBindingResult Declarations,
    ImmutableArray<BoundBlock> Bodies,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public UnitSymbol Unit => Declarations.Unit;

    public Scope? InterfaceScope => Declarations.InterfaceScope;

    public Scope Scope => Declarations.Scope;
}

/// <summary>Binds a compilation unit: declarations (CMP-113), then the unit-level bodies (CMP-114).</summary>
internal static class Binder
{
    public static BindingResult BindCompilationUnit(SyntaxTree tree, BuiltInTypes types)
    {
        var diagnostics = new DiagnosticBag();
        var declarations = DeclarationBinder.BindCompilationUnit(tree, types, diagnostics);
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

        return new BindingResult(declarations, bodies.ToImmutable(), diagnostics.ToImmutable());
    }
}
