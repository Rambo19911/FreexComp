using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>
/// language-service.md §11 (C-142), the initial set: <c>declare-variable</c> and <c>add-uses</c> for E2003,
/// <c>complete-class</c> for E2605, <c>implement-interface</c> for E2651. Each offered action is applied and the
/// result re-checked: the diagnostic it answers is gone and no new error appears.
/// </summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
public sealed class CodeActionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Opens a project (<c>P.pas</c> main), returns the actions for the <c>[|…|]</c> span of <paramref name="path"/>.</summary>
    private static (Workspace Workspace, Document Document, IReadOnlyList<CodeAction> Actions) Actions(
        string path, string markup, params (string Name, string Text)[] others)
    {
        var file = MarkupTestFile.Parse(markup);
        var range = file.Spans.Count > 0 ? file.Spans[0] : new TextSpan(file.Caret, 0);
        var workspace = new Workspace(TestFiles.Files([(path, file.Text), .. others.Select(o => (o.Name, o.Text.ReplaceLineEndings("\n")))]));
        workspace.SetProject(TestFiles.Options());
        var document = workspace.GetDocument(TestFiles.PathOf(path));
        return (workspace, document, CodeActionService.GetCodeActions(document, range, Ct));
    }

    /// <summary>Applies the action's edits through the workspace; returns the new text of each edited document's file.</summary>
    private static string Apply(Workspace workspace, CodeAction action, string path)
    {
        foreach (var edit in action.Edits)
        {
            workspace.UpdateDocument(edit.Path, edit.Changes);
        }

        return workspace.GetDocument(TestFiles.PathOf(path)).Text.Text;
    }

    private static string[] Errors(Workspace workspace, string path)
    {
        var document = workspace.GetDocument(TestFiles.PathOf(path));
        return [.. DiagnosticService.GetSyntaxDiagnostics(document).Concat(DiagnosticService.GetSemanticDiagnostics(document, Ct))
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToText())];
    }

    // ------------------------------------------------------------------ declare-variable

    [Fact]
    public void DeclareVariable_NoVarSection_AddsOneBeforeTheBody()
    {
        var (workspace, _, actions) = Actions("P.pas", """
            program P;

            procedure Run;
            begin
              [|Count|] := 1 + 2;
            end;

            begin
              Run;
            end.
            """);

        var action = Assert.Single(actions, a => a.Id == "declare-variable");
        Assert.Equal("Declare variable 'Count: Integer'", action.Title);
        var text = Apply(workspace, action, "P.pas");
        Assert.Equal("program P;\n\nprocedure Run;\nvar\n  Count: Integer;\nbegin\n  Count := 1 + 2;\nend;\n\nbegin\n  Run;\nend.", text);
        Assert.Empty(Errors(workspace, "P.pas"));
    }

    [Fact]
    public void DeclareVariable_ExistingVarSection_AppendsToIt()
    {
        var (workspace, _, actions) = Actions("P.pas", """
            program P;

            procedure Run(const Name: string);
            var
              I: Integer;
            begin
              I := 0;
              [|S|] := Name;
            end;

            begin
              Run('x');
            end.
            """);

        var action = Assert.Single(actions, a => a.Id == "declare-variable");
        var text = Apply(workspace, action, "P.pas");
        Assert.Contains("var\n  I: Integer;\n  S: string;\nbegin", text, StringComparison.Ordinal);
        Assert.Empty(Errors(workspace, "P.pas"));
    }

    [Fact]
    public void DeclareVariable_ForLoopControlVariable_TakesTheStartType()
    {
        var (workspace, _, actions) = Actions("P.pas", """
            program P;

            procedure Run;
            begin
              for [|K|] := 1 to 3 do
                Writeln(K);
            end;

            begin
              Run;
            end.
            """);

        var action = Assert.Single(actions, a => a.Id == "declare-variable");
        Assert.Contains("var\n  K: Integer;\nbegin", Apply(workspace, action, "P.pas"), StringComparison.Ordinal);
        Assert.Empty(Errors(workspace, "P.pas"));
    }

    [Fact]
    public void DeclareVariable_OutsideARoutine_OrWithoutAnInferableType_IsNotOffered()
    {
        Assert.DoesNotContain(Actions("P.pas", "program P;\nbegin\n  [|Count|] := 1;\nend.").Actions, a => a.Id == "declare-variable");
        Assert.DoesNotContain(Actions("P.pas", "program P;\nprocedure Run;\nbegin\n  [|Missing|];\nend;\nbegin\nend.").Actions,
            a => a.Id == "declare-variable");
    }

    [Fact]
    public void NoDiagnosticInTheRange_NoActions()
    {
        Assert.Empty(Actions("P.pas", "program P;\nvar [|X|]: Integer;\nbegin\n  X := 1;\nend.").Actions);
    }

    // ------------------------------------------------------------------ add-uses

    private const string HelperUnit = """
        unit Helpers;
        interface
        type
          TKind = (kOne, kTwo);
        function Twice(A: Integer): Integer;
        implementation
        function Twice(A: Integer): Integer; begin Result := A * 2 end;
        end.
        """;

    [Fact]
    public void AddUses_ExactlyOneUnitExportsTheName()
    {
        var (workspace, _, actions) = Actions("P.pas", """
            program P;

            begin
              Writeln([|Twice|](2));
            end.
            """, ("Helpers.pas", HelperUnit));

        var action = Assert.Single(actions, a => a.Id == "add-uses");
        Assert.Equal("Add 'Helpers' to the uses clause", action.Title);
        var text = Apply(workspace, action, "P.pas");
        Assert.Equal("program P;\n\nuses\n  Helpers;\n\nbegin\n  Writeln(Twice(2));\nend.", text);
        Assert.Empty(Errors(workspace, "P.pas"));
    }

    [Fact]
    public void AddUses_EnumMemberAndExistingClause()
    {
        var (workspace, _, actions) = Actions("P.pas", """
            program P;
            uses Other;
            var K: TKind;
            begin
              K := [|kTwo|];
            end.
            """, ("Helpers.pas", HelperUnit), ("Other.pas", "unit Other;\ninterface\nimplementation\nend."));

        Assert.Contains(actions, a => a.Id == "add-uses");
        var (workspace2, _, typeActions) = Actions("P.pas", "program P;\nuses Other;\nvar K: [|TKind|];\nbegin\nend.",
            ("Helpers.pas", HelperUnit), ("Other.pas", "unit Other;\ninterface\nimplementation\nend."));
        var action = Assert.Single(typeActions, a => a.Id == "add-uses");
        Assert.Equal("program P;\nuses Other, Helpers;\nvar K: TKind;\nbegin\nend.", Apply(workspace2, action, "P.pas"));
        Assert.DoesNotContain(Errors(workspace2, "P.pas"), e => e.Contains("E2003", StringComparison.Ordinal));
        Assert.NotNull(workspace);
    }

    [Fact]
    public void AddUses_TwoUnitsExportTheName_IsNotOffered()
    {
        var (_, _, actions) = Actions("P.pas", "program P;\nbegin\n  Writeln([|Twice|](2));\nend.",
            ("Helpers.pas", HelperUnit),
            ("More.pas", "unit More;\ninterface\nfunction Twice(A: Integer): Integer;\nimplementation\nfunction Twice(A: Integer): Integer; begin Result := A end;\nend."));

        Assert.DoesNotContain(actions, a => a.Id == "add-uses");
    }

    [Fact]
    public void AddUses_InAUnitImplementation_GoesToTheImplementationClause()
    {
        var (workspace, _, actions) = Actions("U1.pas", """
            unit U1;

            interface

            procedure Run;

            implementation

            procedure Run;
            begin
              Writeln([|Twice|](1));
            end;

            end.
            """, ("P.pas", "program P;\nuses U1;\nbegin\n  Run;\nend."), ("Helpers.pas", HelperUnit));

        var action = Assert.Single(actions, a => a.Id == "add-uses");
        var text = Apply(workspace, action, "U1.pas");
        Assert.Contains("implementation\n\nuses\n  Helpers;\n\nprocedure Run;", text, StringComparison.Ordinal);
        Assert.Empty(Errors(workspace, "U1.pas"));
    }

    // ------------------------------------------------------------------ complete-class

    [Fact]
    public void CompleteClass_GeneratesEveryMissingImplementation()
    {
        var (workspace, _, actions) = Actions("U1.pas", """
            unit U1;

            interface

            type
              TFoo = class
              public
                procedure [|Run|];
                function Twice(const A: Integer): Integer; virtual;
                class procedure Make(var S: string);
              end;

            implementation

            end.
            """, ("P.pas", "program P;\nuses U1;\nbegin\nend."));

        var action = Assert.Single(actions, a => a.Id == "complete-class");
        Assert.Equal("Complete class 'TFoo'", action.Title);
        var text = Apply(workspace, action, "U1.pas");
        Assert.EndsWith("""
            implementation

            procedure TFoo.Run;
            begin
            end;

            function TFoo.Twice(const A: Integer): Integer;
            begin
            end;

            class procedure TFoo.Make(var S: string);
            begin
            end;

            end.
            """.ReplaceLineEndings("\n"), text, StringComparison.Ordinal);
        Assert.Empty(Errors(workspace, "U1.pas"));
    }

    [Fact]
    public void CompleteClass_AfterTheExistingImplementations()
    {
        var (workspace, _, actions) = Actions("U1.pas", """
            unit U1;
            interface
            type
              TFoo = class
                procedure A;
                procedure [|B|];
              end;
            implementation

            procedure TFoo.A;
            begin
            end;

            end.
            """, ("P.pas", "program P;\nuses U1;\nbegin\nend."));

        var action = Assert.Single(actions, a => a.Id == "complete-class");
        Assert.EndsWith("procedure TFoo.A;\nbegin\nend;\n\nprocedure TFoo.B;\nbegin\nend;\n\nend.", Apply(workspace, action, "U1.pas"),
            StringComparison.Ordinal);
        Assert.Empty(Errors(workspace, "U1.pas"));
    }

    // ------------------------------------------------------------------ implement-interface

    [Fact]
    public void ImplementInterface_DeclaresAndImplementsTheMissingMethods()
    {
        var (workspace, _, actions) = Actions("U1.pas", """
            unit U1;

            interface

            type
              IGreeter = interface
                procedure Greet(const Name: string);
                function Count: Integer;
              end;

              TGreeter = class(TInterfacedObject, [|IGreeter|])
              public
                function Count: Integer;
              end;

            implementation

            function TGreeter.Count: Integer;
            begin
              Result := 0;
            end;

            end.
            """, ("P.pas", "program P;\nuses U1;\nbegin\nend."));

        var action = Assert.Single(actions, a => a.Id == "implement-interface");
        Assert.Equal("Implement interface 'IGreeter'", action.Title);
        var text = Apply(workspace, action, "U1.pas");
        Assert.Contains("  public\n    function Count: Integer;\n    procedure Greet(const Name: string);\n  end;", text, StringComparison.Ordinal);
        Assert.Contains("  Result := 0;\nend;\n\nprocedure TGreeter.Greet(const Name: string);\nbegin\nend;\n\nend.", text, StringComparison.Ordinal);
        Assert.Empty(Errors(workspace, "U1.pas"));
    }
}
