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

    /// <summary>
    /// A typed constant's static storage (LANG-344): a read-only global variable with the same name, type and value that
    /// references to the constant bind to. Null for true constants.
    /// </summary>
    internal VariableSymbol? Storage
    {
        get => _storage;
        init
        {
            _storage = value;
            if (value is not null)
            {
                value.TypedConstant = this;                              // the semantic model reports the constant, not its storage
            }
        }
    }

    private readonly VariableSymbol? _storage;

    /// <summary><c>const Name = value</c> or <c>const Name: Type = …</c> (CMP-731).</summary>
    public override string ToDisplayString() =>
        IsTyped ? $"const {Name}: {Type.Name} = …" : $"const {Name} = {Value?.ToDisplayString() ?? "?"}";
}
