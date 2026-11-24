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

    /// <summary>
    /// C-230: for the closure of a plain (nested) routine, the enclosing routines whose closures it keeps (plain and counted fields
    /// <c>$outer.&lt;name&gt;</c>/<c>$outerRef.&lt;name&gt;</c>, set in the prologue): its anonymous methods reach their variables through them.
    /// </summary>
    public Dictionary<RoutineSymbol, (FieldSymbol Plain, FieldSymbol Counted)> Links { get; } = new(ReferenceEqualityComparer.Instance);

    private readonly List<Symbol> _captured = [];
    private readonly List<RoutineSymbol> _linked = [];

    /// <summary>Lays out the fields (<paramref name="captured"/> in order) and the interfaces once the owner's body is bound.</summary>
    public void Complete(BuiltInTypes types, IEnumerable<Symbol> captured)
    {
        _captured.AddRange(captured);
        Layout(types);
    }

    /// <summary>
    /// C-230: after every body of the unit is bound, a variable captured through a nested routine (<paramref name="captured"/>) and a
    /// link to an enclosing routine's closure (<paramref name="link"/>) are added and the class laid out again (nothing is lowered yet).
    /// </summary>
    public bool Extend(Symbol? captured, RoutineSymbol? link)
    {
        var changed = false;
        if (captured is not null && !_captured.Contains(captured))
        {
            _captured.Add(captured);
            changed = true;
        }

        if (link is not null && !_linked.Contains(link))
        {
            _linked.Add(link);
            changed = true;
        }

        return changed;
    }

    /// <summary>The fields and interfaces of the class from the captured variables and links noted so far.</summary>
    public void Layout(BuiltInTypes types)
    {
        Fields.Clear();
        Links.Clear();
        var end = Class.Parent?.InstanceSize ?? types.Target.PointerSize;
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

        foreach (var linked in _linked)
        {
            Links[linked] = (Add("$outer." + linked.Name, linked.Closure!.Class), Add("$outerRef." + linked.Name, types.IInterface ?? types.Pointer));
        }

        foreach (var symbol in _captured)
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

    /// <summary>A new closure for <paramref name="owner"/> (null: a unit-level body), its class registered with <paramref name="unit"/>.</summary>
    public static ClosureInfo Create(RoutineSymbol? owner, UnitSymbol unit, BuiltInTypes types, SyntaxNode syntax)
    {
        var root = types.TInterfacedObject as ClassTypeSymbol;
        ClassTypeSymbol type;
        lock (unit.Closures)
        {
            type = new ClassTypeSymbol(types.Target, (owner?.Name ?? "$body") + ".$closure" + (unit.Closures.Count + 1), unit, [])
            {
                IsForward = false,
                Parent = root,
                VirtualSlots = root?.VirtualSlots ?? [],
                IsTypeInfo = root?.IsTypeInfo ?? false,
                InstanceSize = root?.InstanceSize ?? types.Target.PointerSize,
                IsReferenced = true,
            };
            unit.Closures.Add(type);
        }

        var symbol = (Symbol?)owner ?? unit;
        return new ClosureInfo(owner, type,
            new VariableSymbol("$closure", type, isGlobal: false, symbol, []),
            new VariableSymbol("$closureRef", types.IInterface ?? types.Pointer, isGlobal: false, symbol, []), syntax);
    }

    /// <summary>
    /// C-230 (LANG-721, LANG-725): once every body of <paramref name="unit"/> (or of its generic instances) is bound, a variable reached
    /// from an anonymous method through a nested routine — an anonymous method inside a nested routine using the enclosing routine's
    /// variable, or a nested routine inside an anonymous method — moves to its owner's closure (made when the owner has none), and a
    /// nested routine whose closure is entered on the way keeps a link to the owner's closure. The nested routine then counts as
    /// referencing the variable, so the frames carry the owner's closure to its prologue.
    /// </summary>
    public static ImmutableArray<BoundRoutineBody> ResolveMixedCaptures(IReadOnlyList<BoundRoutineBody> bodies, UnitSymbol unit, BuiltInTypes types)
    {
        var references = new Dictionary<RoutineSymbol, List<Symbol>>(ReferenceEqualityComparer.Instance);
        var changed = new List<ClosureInfo>();
        void Note(ClosureInfo closure)
        {
            if (!changed.Contains(closure))
            {
                changed.Add(closure);
            }
        }

        foreach (var body in bodies)
        {
            foreach (var symbol in body.CapturedVariables)
            {
                if (symbol.ContainingSymbol is not RoutineSymbol owner || ReferenceEquals(owner, body.Routine))
                {
                    continue;
                }

                // the nearest anonymous method between the reader and the owner: below it frames, above it closures
                RoutineSymbol? at = null;
                for (var routine = body.Routine; routine is not null && !ReferenceEquals(routine, owner); routine = LexicalParent(routine))
                {
                    if (routine is MethodSymbol { IsAnonymous: true } anonymous)
                    {
                        at = anonymous.AnonymousParent;
                        break;
                    }
                }

                if (at is null || types.TInterfacedObject is null)
                {
                    continue;
                }

                if (owner.Closure is null)
                {
                    owner.Closure = Create(owner, unit, types, owner.Definition ?? (SyntaxNode)body.Body.Syntax);
                    owner.Closure.Complete(types, []);
                }

                if (owner.Closure.Extend(symbol, link: null))
                {
                    Note(owner.Closure);
                }

                // the walk of LoweringContext.ClosureOf: an anonymous method's closure leads to its parent's ($outer); a nested
                // routine's closure needs a link to the owner's
                while (at is not null && !ReferenceEquals(at, owner))
                {
                    if (at is MethodSymbol { IsAnonymous: true } anonymous)
                    {
                        at = anonymous.AnonymousParent;
                        continue;
                    }

                    if (at.Closure!.Extend(captured: null, owner))
                    {
                        Note(at.Closure);
                    }

                    if (!references.TryGetValue(at, out var list))
                    {
                        references[at] = list = [];
                    }

                    if (!list.Contains(symbol))
                    {
                        list.Add(symbol);
                    }

                    break;
                }
            }
        }

        foreach (var closure in changed)
        {
            closure.Layout(types);
        }

        return [.. bodies.Select(b => references.TryGetValue(b.Routine, out var more)
            ? b with { CapturedVariables = [.. b.CapturedVariables, .. more.Where(s => !b.CapturedVariables.Contains(s))] }
            : b)];
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
