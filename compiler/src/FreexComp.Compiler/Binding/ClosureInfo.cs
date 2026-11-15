using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// CMP-380 (C-120): the heap frame of one routine (or unit-level body, or anonymous method) that contains anonymous methods. The
/// closure class descends from <c>System.TInterfacedObject</c>; its fields are the variables the anonymous methods capture (and,
/// for the closure of an anonymous method, <c>$outer</c>/<c>$outerRef</c>: the closure that method runs on); it implements one
/// interface <c>$I&lt;n&gt;</c> per anonymous method, whose <c>Invoke</c> is the method <c>Anon&lt;n&gt;</c>. The owner creates it in its
/// prologue into <see cref="Local"/> and keeps it alive through the counted <see cref="Keep"/>.
/// </summary>
internal sealed class ClosureInfo
{
    public ClosureInfo(RoutineSymbol? owner, ClassTypeSymbol @class, VariableSymbol local, VariableSymbol keep, SyntaxNode syntax)
    {
        Owner = owner;
        Class = @class;
        Local = local;
        Keep = keep;
        Syntax = syntax;
    }

    /// <summary>The routine whose activations the closure belongs to; null for a unit-level body.</summary>
    public RoutineSymbol? Owner { get; }

    public ClassTypeSymbol Class { get; }

    /// <summary>The hidden local <c>$closure</c> (the instance, not counted).</summary>
    public VariableSymbol Local { get; }

    /// <summary>The hidden local <c>$closureRef: IInterface</c> holding the owner's reference (released at exit).</summary>
    public VariableSymbol Keep { get; }

    /// <summary>The first anonymous method expression (location of synthesised nodes).</summary>
    public SyntaxNode Syntax { get; }

    /// <summary>The closure of the enclosing body, for the closure of an anonymous method (plain and counted).</summary>
    public FieldSymbol? Outer { get; private set; }

    public FieldSymbol? OuterRef { get; private set; }

    /// <summary>The field of each captured variable, parameter or <c>Self</c> of <see cref="Owner"/>.</summary>
    public Dictionary<Symbol, FieldSymbol> Fields { get; } = new(ReferenceEqualityComparer.Instance);

    public List<(MethodSymbol Method, InterfaceTypeSymbol Interface)> Methods { get; } = [];

    public bool IsComplete { get; private set; }

    /// <summary>Lays out the fields (<paramref name="captured"/> in order) and the interfaces once the owner's body is bound.</summary>
    public void Complete(BuiltInTypes types, IEnumerable<Symbol> captured)
    {
        var end = Class.Parent?.InstanceSize ?? 8;
        var fields = ImmutableArray.CreateBuilder<FieldSymbol>();
        FieldSymbol Add(string name, TypeSymbol type)
        {
            var alignment = Math.Max(1, Math.Min(type.Alignment, 8));
            var offset = (end + alignment - 1) / alignment * alignment;
            var field = new FieldSymbol(name, type, offset, Class, [], Visibility.Private) { IsReferenced = true };
            end = offset + type.Size;
            fields.Add(field);
            return field;
        }

        if (Owner is MethodSymbol { IsAnonymous: true, SelfParameter: { } self } && types.IInterface is not null)
        {
            Outer = Add("$outer", self.Type);
            OuterRef = Add("$outerRef", types.IInterface!);
        }

        foreach (var symbol in captured)
        {
            Fields[symbol] = Add(symbol.Name, symbol is VariableSymbol variable ? variable.Type : ((ParameterSymbol)symbol).Type);
        }

        Class.Fields = fields.ToImmutable();
        var offsetOfInterface = (end + 7) / 8 * 8;
        var interfaces = ImmutableArray.CreateBuilder<ImplementedInterface>();
        foreach (var (method, intf) in Methods)
        {
            var implementations = intf.AllMethods.Take(intf.AllMethods.Length - 1)
                .Select(m => DeclarationBinder.FindImplementation(Class, m.Name, m) ?? m).Append(method).ToImmutableArray();
            interfaces.Add(new ImplementedInterface(intf, offsetOfInterface, implementations));
            offsetOfInterface += 8;
        }

        Class.Interfaces = interfaces.ToImmutable();
        Class.Methods = [.. Methods.Select(m => m.Method)];
        Class.InstanceSize = (offsetOfInterface + 7) / 8 * 8;
        IsComplete = true;
    }

    /// <summary>The routine whose body lexically contains <paramref name="routine"/>: an anonymous method's parent, a nested routine's container.</summary>
    public static RoutineSymbol? LexicalParent(RoutineSymbol routine) =>
        routine is MethodSymbol { IsAnonymous: true } anonymous ? anonymous.AnonymousParent : routine.ContainingSymbol as RoutineSymbol;

    /// <summary>The closure classes made for <paramref name="unit"/> (emitted with its classes).</summary>
    public static IEnumerable<ClassTypeSymbol> Of(UnitSymbol unit)
    {
        lock (unit.Closures)
        {
            return [.. unit.Closures];
        }
    }
}
