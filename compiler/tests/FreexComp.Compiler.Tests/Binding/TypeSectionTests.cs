using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Type sections (LANG-205…208): names declared in one section and forward pointers between them.</summary>
[Trait("Phase", "2")]
public sealed class TypeSectionTests
{
    /// <summary>
    /// C-181: LANG-208 + LANG-110: <c>^integer</c> names the <c>Integer</c> declared later in the same section (identifiers are
    /// case-insensitive), so it is a forward pointer to that record, not to the outer built-in <c>Integer</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-110")]
    [Trait("Req", "LANG-208")]
    public void ForwardPointerTargetCaseInsensitive()
    {
        var (result, _) = Bind("type PInt = ^integer; Integer = record V: Int64 end; begin end.");
        Assert.Empty(result.Diagnostics);

        var pointer = Assert.IsType<PointerTypeSymbol>(result.Declarations.Scope.Lookup("PInt"));
        var record = Assert.IsType<RecordTypeSymbol>(result.Declarations.Scope.Lookup("Integer"));
        Assert.Same(record, pointer.PointedType);
    }
}
