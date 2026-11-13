namespace FreexComp.Compiler.Binding;

/// <summary>Classification of a conversion (api.md §6, LANG-280/290).</summary>
public enum ConversionKind { None, Identity, ImplicitNumeric, ImplicitString, ImplicitReference, ImplicitUserDefined,
                             Explicit, MethodGroup, NilLiteral, ArrayConstructor }

/// <summary>A conversion between two types (api.md §6).</summary>
public readonly record struct Conversion(ConversionKind Kind, bool IsImplicit, bool Exists);
