using System.Text.Json.Nodes;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-268 (debug-info-sidecar.md §4): a plain alias (<c>type TMyInt = Integer</c>) is a separate type entry with the alias name,
/// its unit and the target's kind; <c>aliasOf</c> names the target entry. The alias stays the same type as its target
/// (LANG-205): variables of the alias reference the target's entry, and the IR without <c>-g</c> is unchanged.
/// </summary>
public sealed partial class SidecarTests
{
    private const string AliasSource = """
        unit A;
        interface
        type
          TMyInt = Integer;
          TCount = TMyInt;
          TText = string;
          TPoint = record
            X, Y: Integer;
          end;
          TPt = TPoint;
          TBase = class
            F: Integer;
          end;
          TBaseAlias = TBase;
          TDist = type Integer;
          TDistAlias = TDist;
        var
          N: TMyInt;
          C: TCount;
          S: TText;
          P: TPt;
          D: TDistAlias;
        implementation
        end.
        """;

    /// <summary>The same unit with every plain alias replaced by its target (what the alias denotes, LANG-205).</summary>
    private const string AliasFreeSource = """
        unit A;
        interface
        type
          TPoint = record
            X, Y: Integer;
          end;
          TBase = class
            F: Integer;
          end;
          TDist = type Integer;
        var
          N: Integer;
          C: Integer;
          S: string;
          P: TPoint;
          D: TDist;
        implementation
        end.
        """;

    // ------------------------------------------------------------------ Sidecar_AliasEntries

    /// <summary>
    /// debug-info-sidecar.md §4: each plain alias is listed with its name, unit, the target's kind and members, and <c>aliasOf</c>
    /// (an alias of an alias names the inner alias; an alias of a distinct type names that type); variables keep the target's id.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-425")]
    public void Sidecar_AliasEntries()
    {
        var fragment = JsonNode.Parse(EmitWithSidecar(AliasSource, @"C:\p\A.pas").Fragment)!.AsObject();
        var merged = JsonNode.Parse(PdiSidecar.Merge("A", "codeview", [EmitWithSidecar(AliasSource, @"C:\p\A.pas").Fragment]))!.AsObject();

        foreach (var pdi in new[] { fragment, merged })
        {
            ValidateSchema(pdi);
            var types = pdi["types"]!.AsArray().Select(t => t!.AsObject()).ToDictionary(t => t["id"]!.GetValue<int>());
            JsonObject Named(string name) => types.Values.Single(t => t["name"]!.GetValue<string>() == name);
            string Target(string alias) => types[Named(alias)["aliasOf"]!.GetValue<int>()]["name"]!.GetValue<string>();
            string Describe(string name)
            {
                var copy = (JsonObject)Named(name).DeepClone();
                copy.Remove("id");
                copy.Remove("name");
                copy.Remove("unit");
                copy.Remove("aliasOf");
                return copy.ToJsonString();
            }

            Assert.Equal(
                ["TMyInt:A:integer:Integer", "TCount:A:integer:TMyInt", "TText:A:string:string", "TPt:A:record:TPoint",
                 "TBaseAlias:A:class:TBase", "TDistAlias:A:integer:TDist"],
                new[] { "TMyInt", "TCount", "TText", "TPt", "TBaseAlias", "TDistAlias" }
                    .Select(a => $"{a}:{Named(a)["unit"]}:{Named(a)["kind"]}:{Target(a)}"));
            // the alias carries the target's kind and members (size, fields, ancestor, ...)
            Assert.Equal(Describe("Integer"), Describe("TMyInt"));
            Assert.Equal(Describe("Integer"), Describe("TCount"));
            Assert.Equal(Describe("string"), Describe("TText"));
            Assert.Equal(Describe("TPoint"), Describe("TPt"));
            Assert.Equal(Describe("TBase"), Describe("TBaseAlias"));
            Assert.Equal(Describe("TDist"), Describe("TDistAlias"));
            Assert.Equal("A", Named("TDist")["unit"]!.GetValue<string>());
            Assert.Null(Named("TDist")["aliasOf"]);
            Assert.Null(Named("Integer")["aliasOf"]);

            // type identity is unchanged: a variable of an alias references the target's entry
            var globals = pdi["units"]![0]!["globals"]!.AsArray().ToDictionary(g => g!["name"]!.GetValue<string>(), g => types[g!["type"]!.GetValue<int>()]["name"]!.GetValue<string>());
            Assert.Equal(["C:Integer", "D:TDist", "N:Integer", "P:TPoint", "S:string"], globals.Select(g => $"{g.Key}:{g.Value}").Order(StringComparer.Ordinal));
        }

        Assert.Equal(EmitWithSidecar(AliasSource, @"C:\p\A.pas").Fragment, EmitWithSidecar(AliasSource, @"C:\p\A.pas").Fragment);
    }

    /// <summary>LANG-205: an alias emits nothing — without <c>-g</c> the unit's IR equals that of the unit written with the targets.</summary>
    [Fact]
    [Trait("Req", "CMP-425")]
    public void Sidecar_AliasEntries_IrWithoutDebugInfoUnchanged()
    {
        static string Ir(string source)
        {
            var tree = SyntaxTree.Parse(SourceText.From(@"C:\p\A.pas", source));
            Assert.Empty(tree.Diagnostics);
            var result = BindWithStubSystem(tree);
            Assert.Empty(Format(result.Diagnostics));
            var writer = new LlvmTextWriter();
            Assert.Null(IrEmitter.EmitUnit(Lowerer.Lower(result), writer));
            return writer.GetText();
        }

        Assert.Equal(Ir(AliasFreeSource), Ir(AliasSource));
    }
}
