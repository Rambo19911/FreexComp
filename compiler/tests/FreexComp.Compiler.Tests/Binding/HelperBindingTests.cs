// The model's lookups are synchronous index reads over an already bound compilation; the optional token is left out on purpose.
#pragma warning disable xUnit1051

using System.Text.RegularExpressions;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// Class and record helpers, completion (phase8/helpers-completion.md §15; cards F10-027…029): helper inheritance (LANG-990 (e))
/// and the active helper of a type at a source position (LANG-993), C-438; the kinds of helped types (LANG-990 (b)), C-442;
/// mutating methods, kinds of receiver and E2752 (LANG-996), C-443.
/// </summary>
[Trait("Phase", "8")]
public sealed partial class HelperBindingTests
{
    // ------------------------------------------------------------------ LANG-990 (e): the ancestor of a helper

    /// <summary>The declaration under test is line 16: <c>  TH = {0} end;</c>.</summary>
    private const string AncestorProgram = """
        program P;
        type
          TBase = class end;
          TMid = class(TBase) end;
          TLeaf = class(TMid) end;
          TOther = class end;
          TRec = record X: Integer; end;
          TBaseH = class helper for TBase end;
          TMidH = class helper for TMid end;
          TLeafH = class helper for TLeaf end;
          TOtherH = class helper for TOther end;
          TIntH = record helper for Integer end;
          TRecH = record helper for TRec end;
          TBox<T> = class end;
          TIntBoxH = class helper for TBox<Integer> end;
          TH = {0} end;
        begin
        end.
        """;

    /// <summary>
    /// LANG-990 (e): <c>class helper(A) for T</c> — <c>A</c> must name a class helper for <c>T</c> or for an ancestor class of
    /// <c>T</c>; everything else that <c>A</c> names is E2753 at <c>A</c>, as is any ancestor of a record helper; an undeclared name
    /// is E2003, two names are E2900. After E2753 the helper is declared without an ancestor.
    /// </summary>
    [Theory]
    [InlineData("same class", "class helper(TMidH) for TMid", "", "TMidH")]
    [InlineData("ancestor class", "class helper(TBaseH) for TMid", "", "TBaseH")]
    [InlineData("ancestor of the ancestor class", "class helper(TBaseH) for TLeaf", "", "TBaseH")]
    [InlineData("unit-qualified helper name", "class helper(P.TMidH) for TMid", "", "TMidH")]
    [InlineData("same instance of a generic class", "class helper(TIntBoxH) for TBox<Integer>", "", "TIntBoxH")]
    [InlineData("another instance of the generic class", "class helper(TIntBoxH) for TBox<string>", "E2753(16,21): Helper 'TH' cannot inherit from 'TIntBoxH'", null)]
    [InlineData("descendant class", "class helper(TLeafH) for TMid", "E2753(16,21): Helper 'TH' cannot inherit from 'TLeafH'", null)]
    [InlineData("unrelated class", "class helper(TOtherH) for TMid", "E2753(16,21): Helper 'TH' cannot inherit from 'TOtherH'", null)]
    [InlineData("record helper as ancestor", "class helper(TIntH) for TMid", "E2753(16,21): Helper 'TH' cannot inherit from 'TIntH'", null)]
    [InlineData("a class as ancestor", "class helper(TBase) for TMid", "E2753(16,21): Helper 'TH' cannot inherit from 'TBase'", null)]
    [InlineData("a predeclared type as ancestor", "class helper(Integer) for TMid", "E2753(16,21): Helper 'TH' cannot inherit from 'Integer'", null)]
    [InlineData("a constant as ancestor", "class helper(True) for TMid", "E2753(16,21): Helper 'TH' cannot inherit from 'True'", null)]
    [InlineData("unit-qualified name of a class", "class helper(P.TBase) for TMid", "E2753(16,21): Helper 'TH' cannot inherit from 'P.TBase'", null)]
    [InlineData("itself", "class helper(TH) for TMid", "E2753(16,21): Helper 'TH' cannot inherit from 'TH'", null)]
    [InlineData("record helper with a record-helper ancestor", "record helper(TRecH) for TRec", "E2753(16,22): Helper 'TH' cannot inherit from 'TRecH'", null)]
    [InlineData("record helper with a class-helper ancestor", "record helper(TBaseH) for Integer", "E2753(16,22): Helper 'TH' cannot inherit from 'TBaseH'", null)]
    [InlineData("undeclared name", "class helper(TMissing) for TMid", "E2003(16,21): Undeclared identifier 'TMissing'", null)]
    [InlineData("undeclared name, record helper", "record helper(TMissing) for TRec", "E2003(16,22): Undeclared identifier 'TMissing'", null)]
    [InlineData("two names", "class helper(TBaseH, TMidH) for TMid", "E2900(16,20): Feature not supported: helper ancestor list", null)]
    [InlineData("two names, record helper", "record helper(TRecH, TIntH) for TRec", "E2900(16,21): Feature not supported: helper ancestor list", null)]
    [Trait("Req", "LANG-990")]
    public void Ancestor_Rules(string what, string declaration, string expected, string? parent)
    {
        var (result, _) = BinderTestHelper.Bind(AncestorProgram.Replace("{0}", declaration, StringComparison.Ordinal));

        Assert.Equal(expected.Length == 0 ? [] : [expected], BinderTestHelper.Format(result.Diagnostics));
        var helper = result.Unit.ImplementationMembers.Concat(result.Unit.InterfaceMembers).OfType<HelperTypeSymbol>().Single(h => h.Name == "TH");
        Assert.True(parent == helper.Parent?.Name, $"{what}: ancestor '{helper.Parent?.Name}'");
    }

    // ------------------------------------------------------------------ LANG-993: the active helper

    private const string Dir = @"C:\p";

    private const string Rtl = @"C:\rtl";

    private const string UShape = """
        unit UShape;
        interface
        type
          TShape = class end;
          TCircle = class(TShape) end;
        implementation
        end.
        """;

    /// <summary><c>THx = class helper[(ancestor)] for T</c> with <c>function Tag</c> (unless <paramref name="tag"/> is off) and <c>function {only}</c>.</summary>
    private static string Declare(string helper, string only, string? ancestor = null, string type = "TShape", bool tag = true) =>
        $"type\n  {helper} = class helper{(ancestor is null ? "" : $"({ancestor})")} for {type}\n"
        + (tag ? "    function Tag: string;\n" : "") + $"    function {only}: string;\n  end;\n";

    private static string Bodies(string helper, string only, bool tag = true) =>
        (tag ? $"function {helper}.Tag: string; begin Result := '{helper}' end;\n" : "")
        + $"function {helper}.{only}: string; begin Result := '{only}' end;\n";

    /// <summary>A unit whose interface declares one class helper (<see cref="Declare"/>).</summary>
    private static string HelperUnit(string unit, string helper, string only, string uses = "UShape", string? ancestor = null,
        string type = "TShape", bool tag = true) =>
        $"unit {unit};\ninterface\nuses {uses};\n{Declare(helper, only, ancestor, type, tag)}implementation\n{Bodies(helper, only, tag)}end.\n";

    private static readonly string UA = HelperUnit("UA", "THA", "OnlyA");
    private static readonly string UB = HelperUnit("UB", "THB", "OnlyB");
    private static readonly string UC = HelperUnit("UC", "THC", "OnlyC", uses: "UShape, UA", ancestor: "THA");

    /// <summary>
    /// The rows of <see cref="ActiveHelper_NearestWins"/>: the files of one program (<c>P.pas</c> is the main file). In a source,
    /// <c>X.Name{=THelper}</c> expects the member access to bind to the member <c>Name</c> of that helper, <c>X.Name{!CODE}</c>
    /// expects the diagnostic <c>CODE</c> at <c>Name</c>; the compilation has no other diagnostic.
    /// </summary>
    private static readonly Dictionary<string, (string File, string Text)[]> ActiveHelperCases = new()
    {
        ["later unit in the uses clause"] =
        [
            ("UShape.pas", UShape), ("UA.pas", UA), ("UB.pas", UB),
            ("UPlain.pas", "unit UPlain;\ninterface\nconst Plain = 1;\nimplementation\nend.\n"),
            ("P.pas", """
                program P;
                uses UShape, UA, UB, UPlain;
                function View(S: TShape; C: TCircle): string;
                begin
                  Result := S.Tag{=THB} + S.OnlyB{=THB} + C.Tag{=THB} + C.OnlyB{=THB} + S.OnlyA{!E2005};
                end;
                begin
                end.
                """),
        ],
        ["the same units in the other order"] =
        [
            ("UShape.pas", UShape), ("UA.pas", UA), ("UB.pas", UB),
            ("P.pas", """
                program P;
                uses UB, UShape, UA;
                function View(S: TShape; C: TCircle): string;
                begin
                  Result := S.Tag{=THA} + S.OnlyA{=THA} + C.Tag{=THA} + S.OnlyB{!E2005};
                end;
                begin
                end.
                """),
        ],
        ["implementation uses after interface uses"] =
        [
            ("UShape.pas", UShape), ("UA.pas", UA), ("UB.pas", UB),
            ("V.pas", """
                unit V;
                interface
                uses UShape, UA;
                function View(S: TShape): string;
                implementation
                uses UB;
                function View(S: TShape): string;
                begin
                  Result := S.Tag{=THB} + S.OnlyB{=THB} + S.OnlyA{!E2005};
                end;
                end.
                """),
            ("W.pas", """
                unit W;
                interface
                uses UShape, UB;
                function View(S: TShape): string;
                implementation
                uses UA;
                function View(S: TShape): string;
                begin
                  Result := S.Tag{=THA} + S.OnlyA{=THA} + S.OnlyB{!E2005};
                end;
                end.
                """),
            ("P.pas", "program P;\nuses V, W;\nbegin\nend.\n"),
        ],
        ["own unit before used units"] =
        [
            ("UShape.pas", UShape), ("UA.pas", UA),
            ("V.pas", $$"""
                unit V;
                interface
                uses UShape;
                {{Declare("THV", "OnlyV")}}
                function View(S: TShape): string;
                implementation
                uses UA;
                {{Bodies("THV", "OnlyV")}}
                function View(S: TShape): string;
                begin
                  Result := S.Tag{=THV} + S.OnlyV{=THV} + S.OnlyA{!E2005};
                end;
                end.
                """),
            ("P.pas", $$"""
                program P;
                uses UShape, V, UA;
                {{Declare("THP", "OnlyP")}}
                {{Bodies("THP", "OnlyP")}}
                function View(S: TShape; C: TCircle): string;
                begin
                  Result := S.Tag{=THP} + C.OnlyP{=THP} + S.OnlyA{!E2005} + S.OnlyV{!E2005};
                end;
                begin
                end.
                """),
        ],
        ["a later declaration in the same program, from its declaration on"] =
        [
            ("UShape.pas", UShape), ("UA.pas", UA),
            ("P.pas", $$"""
                program P;
                uses UShape, UA;
                function Before(S: TShape): string;
                begin
                  Result := S.Tag{=THA} + S.OnlyA{=THA};
                end;
                {{Declare("TH1", "Only1")}}
                {{Bodies("TH1", "Only1")}}
                function Between(S: TShape): string;
                begin
                  Result := S.Tag{=TH1} + S.Only1{=TH1} + S.OnlyA{!E2005};
                end;
                {{Declare("TH2", "Only2", ancestor: "TH1")}}
                {{Bodies("TH2", "Only2")}}
                function After(S: TShape): string;
                begin
                  Result := S.Tag{=TH2} + S.Only2{=TH2} + S.Only1{=TH1};
                end;
                var
                  G: TShape;
                begin
                  if G.Tag{=TH2} = '' then
                    G := nil;
                end.
                """),
        ],
        ["a later declaration in the same unit, both parts"] =
        [
            ("UShape.pas", UShape),
            ("V.pas", $$"""
                unit V;
                interface
                uses UShape;
                {{Declare("THI", "OnlyI")}}
                function Early(S: TShape): string;
                function Late(S: TShape): string;
                implementation
                {{Bodies("THI", "OnlyI")}}
                function Early(S: TShape): string;
                begin
                  Result := S.Tag{=THI} + S.OnlyI{=THI};
                end;
                {{Declare("THM", "OnlyM")}}
                {{Bodies("THM", "OnlyM")}}
                function Late(S: TShape): string;
                begin
                  Result := S.Tag{=THM} + S.OnlyM{=THM} + S.OnlyI{!E2005};
                end;
                end.
                """),
            ("P.pas", """
                program P;
                uses UShape, V;
                function View(S: TShape): string;
                begin
                  Result := S.Tag{=THI} + S.OnlyI{=THI} + S.OnlyM{!E2005};
                end;
                begin
                end.
                """),
        ],
        ["a helper of an implementation part is invisible to users"] =
        [
            ("UShape.pas", UShape), ("UA.pas", UA),
            ("V.pas", $$"""
                unit V;
                interface
                uses UShape;
                function View(S: TShape): string;
                implementation
                {{Declare("THM", "OnlyM")}}
                {{Bodies("THM", "OnlyM")}}
                function View(S: TShape): string;
                begin
                  Result := S.Tag{=THM} + S.OnlyM{=THM};
                end;
                end.
                """),
            ("W.pas", """
                unit W;
                interface
                uses UShape, V;
                function View(S: TShape): string;
                implementation
                function View(S: TShape): string;
                begin
                  Result := S.Tag{!E2005};
                end;
                end.
                """),
            ("P.pas", """
                program P;
                uses UShape, UA, V, W;
                function View(S: TShape): string;
                begin
                  Result := S.Tag{=THA} + S.OnlyA{=THA} + S.OnlyM{!E2005};
                end;
                begin
                end.
                """),
        ],
        ["a unit not named in the uses clause brings no helper"] =
        [
            ("UShape.pas", UShape), ("UA.pas", UA),
            ("UMake.pas", """
                unit UMake;
                interface
                uses UShape, UA;
                function Make: TShape;
                function Show(S: TShape): string;
                implementation
                function Make: TShape;
                begin
                  Result := TShape.Create;
                end;
                function Show(S: TShape): string;
                begin
                  Result := S.Tag{=THA} + Make.OnlyA{=THA};
                end;
                end.
                """),
            ("P.pas", """
                program P;
                uses UShape, UMake;
                function View(S: TShape): string;
                begin
                  Result := S.Tag{!E2005} + Make.OnlyA{!E2005};
                end;
                begin
                end.
                """),
        ],
        ["the chain of the active helper, the ancestor's unit not used"] =
        [
            ("UShape.pas", UShape), ("UA.pas", UA), ("UC.pas", UC),
            ("P.pas", """
                program P;
                uses UShape, UC;
                function View(S: TShape; C: TCircle): string;
                begin
                  Result := S.Tag{=THC} + S.OnlyC{=THC} + S.OnlyA{=THA} + C.OnlyA{=THA};
                end;
                begin
                end.
                """),
        ],
        ["a helper outside the chain contributes nothing"] =
        [
            ("UShape.pas", UShape), ("UA.pas", UA), ("UB.pas", UB), ("UC.pas", UC),
            ("V.pas", """
                unit V;
                interface
                uses UShape, UC, UB;
                function View(S: TShape): string;
                implementation
                function View(S: TShape): string;
                begin
                  Result := S.Tag{=THB} + S.OnlyB{=THB} + S.OnlyC{!E2005} + S.OnlyA{!E2005};
                end;
                end.
                """),
            ("P.pas", """
                program P;
                uses UShape, UA, UB, UC, V;
                function View(S: TShape): string;
                begin
                  Result := S.Tag{=THC} + S.OnlyC{=THC} + S.OnlyA{=THA} + S.OnlyB{!E2005};
                end;
                begin
                end.
                """),
        ],
        ["the chain of a descendant's helper before the helper of the ancestor class"] =
        [
            ("UShape.pas", UShape), ("UA.pas", UA), ("UB.pas", UB),
            ("UD.pas", HelperUnit("UD", "THD", "OnlyD", uses: "UShape, UA", ancestor: "THA", type: "TCircle", tag: false)),
            ("P.pas", """
                program P;
                uses UShape, UB, UD;
                function View(S: TShape; C: TCircle): string;
                begin
                  Result := S.Tag{=THB} + S.OnlyD{!E2005} + C.OnlyD{=THD} + C.Tag{=THA} + C.OnlyA{=THA} + C.OnlyB{=THB};
                end;
                begin
                end.
                """),
        ],
        ["System is first of all"] =
        [
            ("System.pas", """
                unit System;
                interface
                type
                  TSysH = record helper for Integer
                    function Who: Integer;
                  end;
                var Input, Output: Text;
                implementation
                function TSysH.Who: Integer; begin Result := 0 end;
                end.
                """),
            ("UI.pas", """
                unit UI;
                interface
                type
                  TUnitH = record helper for Integer
                    function Who: Integer;
                  end;
                implementation
                function TUnitH.Who: Integer; begin Result := 1 end;
                end.
                """),
            ("V.pas", """
                unit V;
                interface
                function OfSystem(N: Integer): Integer;
                implementation
                function OfSystem(N: Integer): Integer;
                begin
                  Result := N.Who{=TSysH};
                end;
                end.
                """),
            ("P.pas", """
                program P;
                uses V, UI;
                function OfUnit(N: Integer): Integer;
                begin
                  Result := N.Who{=TUnitH};
                end;
                begin
                end.
                """),
        ],
    };

    public static TheoryData<string> ActiveHelperRows => [.. ActiveHelperCases.Keys];

    [GeneratedRegex(@"\{([=!])(\w+)\}")]
    private static partial Regex Marker();

    /// <summary>
    /// LANG-993: one helper is active per type and position — the last one declared before the position in the current unit
    /// (an implementation-part helper from its declaration on, in that part only), else the one of the unit named last among the
    /// uses clauses in effect; units that are not used bring none; the chain of the active helper comes with it, other helpers
    /// contribute nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(ActiveHelperRows))]
    [Trait("Req", "LANG-993")]
    public void ActiveHelper_NearestWins(string row) => AssertMarkers(ActiveHelperCases[row]);

    /// <summary>The compilation of the files of one program (<c>P.pas</c> is the main file; a <c>System.pas</c> replaces the stub).</summary>
    private static Compilation MarkerCompilation((string File, string Text)[] files)
    {
        var fs = new InMemoryFileSystem();
        if (!files.Any(f => f.File == "System.pas"))
        {
            fs.AddSystem();
        }

        foreach (var (file, text) in files)
        {
            fs.Add(Path.Combine(file == "System.pas" ? Rtl : Dir, file), text);          // a row may bring its own System
        }

        var options = new CompilationOptions { MainFile = Path.Combine(Dir, "P.pas"), RtlDirectory = Rtl, ObjectDirectory = @"C:\p\obj" };
        return Compilation.Create(options, [], fs);
    }

    /// <summary>
    /// Compiles the files of one program (<c>P.pas</c> is the main file; a <c>System.pas</c> replaces the stub) and checks the
    /// markers of their sources: <c>X.Name{=THelper}</c> — the member access binds to the member <c>Name</c> of that helper;
    /// <c>X.Name{!CODE}</c> — the diagnostic <c>CODE</c> at <c>Name</c>. The compilation has no other diagnostic.
    /// </summary>
    private static void AssertMarkers((string File, string Text)[] files)
    {
        var compilation = MarkerCompilation(files);
        var expectedBindings = new List<string>();
        var bindings = new List<string>();
        var expectedDiagnostics = new List<string>();
        foreach (var (file, text) in files)
        {
            var model = compilation.GetSemanticModel(Path.Combine(file == "System.pas" ? Rtl : Dir, file));
            var source = model.SyntaxTree.Text;
            foreach (Match marker in Marker().Matches(source.Text))
            {
                var start = marker.Index;
                while (char.IsAsciiLetterOrDigit(source.Text[start - 1]))
                {
                    start--;
                }

                var name = source.Text[start..marker.Index];
                var (line, column) = (source.Text[..start].Count(c => c == '\n') + 1, start - source.Text.LastIndexOf('\n', start));
                var at = $"{file}({line},{column}) {name}";
                if (marker.Groups[1].Value == "!")
                {
                    expectedDiagnostics.Add($"{file}({line},{column}): {marker.Groups[2].Value}");
                    continue;
                }

                var access = model.SyntaxTree.Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single(n => n.Name.Span.Start == start);
                var symbol = model.GetSymbolInfo(access).Symbol;
                expectedBindings.Add($"{at} = {marker.Groups[2].Value}.{name}");
                bindings.Add($"{at} = {symbol?.ContainingSymbol?.Name}.{symbol?.Name}");
            }
        }

        var diagnostics = compilation.GetDiagnostics()
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code}");
        Assert.Equal(expectedBindings, bindings);
        Assert.Equal(expectedDiagnostics.Order(StringComparer.Ordinal), diagnostics.Order(StringComparer.Ordinal));
    }
}
