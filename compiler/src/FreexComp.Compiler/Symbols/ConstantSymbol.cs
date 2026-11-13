using System.Collections.Immutable;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Symbols;

/// <summary>A true constant (<see cref="IsTyped"/> false) or a typed constant (LANG-340…345).</summary>
public sealed class ConstantSymbol : Symbol
{
    internal ConstantSymbol(
        string name,
        TypeSymbol type,
        ConstantValue? value,
        bool isTyped,
        Symbol? containingSymbol,
        ImmutableArray<Location> locations,
        Visibility visibility = Visibility.Public)
        : base(SymbolKind.Constant, name, containingSymbol, locations, visibility)
    {
        Type = type;
        Value = value;
        IsTyped = isTyped;
    }

    public TypeSymbol Type { get; }

    /// <summary>Null when the value could not be computed (an error was reported).</summary>
    public ConstantValue? Value { get; }

    public bool IsTyped { get; }

    /// <summary><c>const Name = value</c> or <c>const Name: Type = …</c> (CMP-731).</summary>
    public override string ToDisplayString() =>
        IsTyped ? $"const {Name}: {Type.Name} = …" : $"const {Name} = {Value?.ToDisplayString() ?? "?"}";
}
