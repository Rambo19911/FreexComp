namespace FreexComp.LanguageService.Tests;

// CMP-1495 (C-446; phase8/helpers-completion.md §9, §15.9): helpers in the structure view.
public sealed partial class DocumentSymbolTests
{
    /// <summary>
    /// CMP-1495: a helper is a type node (kind <c>Type</c>, detail <c>class helper for TFoo</c>, with an ancestor helper
    /// <c>class helper(TFooHelper) for TFoo</c>, <c>record helper for Integer</c>) with its members as children, like a class — visibility
    /// sections, nested types, constants, class vars, methods, the class constructor and properties.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1495")]
    public void HelperType_IsATypeNodeWithItsMembers()
    {
        const string text = """
            unit Unit1;
            interface
            type
              TFoo = class
              end;
              TFooHelper = class helper for TFoo
              strict private
                procedure Hidden;
              public
                type
                  TKind = (kOne, kTwo);
                const
                  Limit = 10;
                class var
                  Count: Integer;
                class constructor Init;
                procedure Show;
                function GetSize: Integer;
                property Size: Integer read GetSize;
              end;
              TFooMoreHelper = class helper(TFooHelper) for TFoo
                procedure More;
              end;
              TIntHelper = record helper for Integer
                function Twice: Integer;
              end;
            implementation
            end.
            """;

        Assert.Equal("""
            Unit Unit1
              Keyword interface
                Class TFoo : class
                Type TFooHelper : class helper for TFoo
                  Keyword strict private
                    Method Hidden : procedure Hidden
                  Keyword public
                    Enum TKind
                      EnumMember kOne
                      EnumMember kTwo
                    Constant Limit
                    Field Count : Integer
                    Method Init : class constructor
                    Method Show : procedure Show
                    Method GetSize : function GetSize: Integer
                    Property Size : Integer
                Type TFooMoreHelper : class helper(TFooHelper) for TFoo
                  Method More : procedure More
                Type TIntHelper : record helper for Integer
                  Method Twice : function Twice: Integer
              Keyword implementation
            """.ReplaceLineEndings("\n"), Outline(text.ReplaceLineEndings("\n")));
    }
}
