using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §2: the semantic overlay (C-132).</summary>
public sealed partial class ClassifierTests
{
    private const string SemanticProgram = """
        program P;
        uses U1;
        type
          TColor = (Red, Green);
          TRec = record F: Integer; end;
          TFoo = class
            Count: Integer;
            procedure Bump;
            property Size: Integer read Count;
          end;
          IThing = interface end;
          TBox<T> = record Item: T; end;
        const
          N = 3;
        var
          G: Integer;
          C: TColor;
        procedure Old; deprecated;
        begin
        end;
        procedure TFoo.Bump;
        begin
          Inc(Count);
        end;
        function Twice(V: Integer): Integer;
        var L: Integer;
        begin
          L := V;
          Result := L * N;
        end;
        var R: TRec; Foo: TFoo; B: TBox<Integer>;
        begin
          C := Green;
          R.F := Twice(G);
          Foo.Bump;
          G := Foo.Size + Shared;
          Old;
          Missing := 1;
        end.
        """;

    private static (string, ClassificationKind)[] Semantic(string text, params (string Name, string Text)[] units)
    {
        var workspace = new Workspace(TestFiles.Files([("P.pas", text), .. units]));
        workspace.SetProject(TestFiles.Options());
        var document = workspace.GetDocument(TestFiles.PathOf("P.pas"));
        var spans = Classifier.GetSemanticClassifications(document, new TextSpan(0, text.Length), TestContext.Current.CancellationToken);
        return [.. spans.Select(s => (text.Substring(s.Span.Start, s.Span.Length), s.Kind))];
    }

    [Fact]
    [Trait("Phase", "3")]
    [Trait("Req", "CMP-710")]
    public void Semantic_EveryKind()
    {
        var text = SemanticProgram.ReplaceLineEndings("\n");
        var spans = Semantic(text, ("U1.pas", "unit U1;\ninterface\nvar Shared: Integer;\nimplementation\nend."));

        (string, ClassificationKind)[] expected =
        [
            ("U1", ClassificationKind.UnitName), ("TColor", ClassificationKind.EnumName), ("Red", ClassificationKind.EnumMember),
            ("TRec", ClassificationKind.TypeName), ("F", ClassificationKind.Field), ("Integer", ClassificationKind.TypeName),
            ("TFoo", ClassificationKind.ClassName), ("Count", ClassificationKind.Field), ("Bump", ClassificationKind.Method),
            ("Size", ClassificationKind.Property), ("IThing", ClassificationKind.InterfaceName), ("N", ClassificationKind.Constant),
            ("G", ClassificationKind.GlobalVariable), ("Old", ClassificationKind.Routine), ("Old", ClassificationKind.DeprecatedSymbol),
            ("Inc", ClassificationKind.Routine), ("Twice", ClassificationKind.Routine), ("V", ClassificationKind.Parameter),
            ("L", ClassificationKind.LocalVariable), ("Green", ClassificationKind.EnumMember), ("Shared", ClassificationKind.GlobalVariable),
            ("Missing", ClassificationKind.UnresolvedIdentifier),
        ];
        foreach (var piece in expected)
        {
            Assert.Contains(piece, spans);
        }

        Assert.DoesNotContain(spans, s => s.Item1 is "read" or "deprecated");      // directives are syntactic
        Assert.DoesNotContain(spans, s => s.Item1 == "TBox" && s.Item2 == ClassificationKind.UnresolvedIdentifier);
    }

    [Fact]
    [Trait("Phase", "3")]
    [Trait("Req", "CMP-711")]
    public void Semantic_RangeLimitsTheSpans()
    {
        var text = SemanticProgram.ReplaceLineEndings("\n");
        var workspace = new Workspace(TestFiles.Files(("P.pas", text), ("U1.pas", "unit U1;\ninterface\nvar Shared: Integer;\nimplementation\nend.")));
        workspace.SetProject(TestFiles.Options());
        var start = text.IndexOf("C := Green", StringComparison.Ordinal);

        var spans = Classifier.GetSemanticClassifications(workspace.GetDocument(TestFiles.PathOf("P.pas")), new TextSpan(start, 10), TestContext.Current.CancellationToken);

        Assert.Equal([ClassificationKind.GlobalVariable, ClassificationKind.EnumMember], spans.Select(s => s.Kind));
    }

    [Fact]
    [Trait("Phase", "3")]
    public void Semantic_AttributeNames()
    {
        var text = "program P;\ntype\n  [Volatile]\n  TFoo = class end;\nbegin\nend.";

        Assert.Contains(("Volatile", ClassificationKind.Attribute), Semantic(text));
    }

    [Fact]
    [Trait("Phase", "3")]
    public void Semantic_BrokenCodeNeverThrows()
    {
        Assert.NotNull(Semantic("program P;\nprocedure Q(; begin X := ; end;\nbegin Q( end"));
    }
}
