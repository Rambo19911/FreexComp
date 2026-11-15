using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-243 (LANG-142, CMP-730): the binder records a declaration's doc comment in <see cref="Symbol.Documentation"/>.</summary>
[Trait("Phase", "3")]
public sealed class SymbolDocumentationTests
{
    private const string Source = """
        unit U;
        interface
        type
          /// A colour.
          TColor = (
            /// The first one.
            Red, Green);
          { not a doc comment }
          TPlain = class
          public
            /// How many.
            Count: Integer;
            {** Runs it.
                Twice. }
            procedure Run;
            /// The count again.
            property Total: Integer read Count;
          end;
        const
          {** The answer. }
          Answer = 42;
        var
          /// Shared state.
          Shared: Integer;
        /// Doubles.
        function Twice(A: Integer): Integer;
        implementation
        procedure TPlain.Run; begin end;
        /// Ignored: the interface heading's comment wins.
        function Twice(A: Integer): Integer; begin Result := A end;
        /// Local helper.
        procedure Helper; begin end;
        end.
        """;

    [Fact]
    [Trait("Req", "CMP-730")]
    public void DocCommentIsRecorded()
    {
        var (result, _) = Bind(Source.ReplaceLineEndings("\n"));
        var unit = result.Unit;
        Symbol Member(string name) => unit.InterfaceMembers.Concat(unit.ImplementationMembers).First(m => m.Name == name);
        var plain = (ClassTypeSymbol)Member("TPlain");
        var color = (EnumTypeSymbol)Member("TColor");

        Assert.Equal("A colour.", color.Documentation);
        Assert.Equal("The first one.", color.EnumMembers[0].Documentation);
        Assert.Null(color.EnumMembers[1].Documentation);
        Assert.Null(plain.Documentation);
        Assert.Equal("How many.", plain.Fields.Single().Documentation);
        Assert.Equal("Runs it.\nTwice.", plain.Methods.Single(m => m.Name == "Run").Documentation);
        Assert.Equal("The count again.", plain.Properties.Single().Documentation);
        Assert.Equal("The answer.", Member("Answer").Documentation);
        Assert.Equal("Shared state.", Member("Shared").Documentation);
        Assert.Equal("Doubles.", Member("Twice").Documentation);
        Assert.Equal("Local helper.", Member("Helper").Documentation);
    }
}
