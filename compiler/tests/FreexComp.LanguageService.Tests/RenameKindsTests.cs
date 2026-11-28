using FreexComp.Compiler;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>
/// CMP-1512 (C-451): what a rename edits for each kind of symbol — one fact per row of the binding table. Each file is written
/// with markup: <c>$$</c> is the position the rename starts at, <c>[|…|]</c> marks exactly the names it must edit. Every fact
/// asserts that edit set (nothing else in any file), that the renamed project has no error, and that every renamed name binds
/// to a symbol of the new name.
/// </summary>
[Trait("Phase", "8")]
[Trait("Req", "CMP-1512")]
public sealed class RenameKindsTests
{
    /// <summary>
    /// Renames from the <c>$$</c> of <paramref name="file"/> to <paramref name="newName"/> and checks the edit set, the build and the
    /// binding of every renamed name. Files are given as markup; <c>P.pas</c> is the main file.
    /// </summary>
    private static void AssertRename(string file, string newName, params (string Name, string Markup)[] markup)
    {
        var parsed = markup.Select(m => (m.Name, File: MarkupTestFile.Parse(m.Markup))).ToList();
        (string Name, string Text)[] files = [.. parsed.Select(p => (p.Name, p.File.Text))];
        var caret = parsed.Single(p => p.Name == file).File.Caret;
        Assert.True(caret >= 0, "no $$ in " + file);
        var doc = MarkupTestFile.Open(file, files);
        var ct = TestContext.Current.CancellationToken;

        var result = RenameService.Rename(doc, caret, newName, ct);

        Assert.True(result.Success, result.Error);
        var expected = parsed.Where(p => p.File.Spans.Count > 0)
            .Select(p => $"{p.Name}: {string.Join(" ", p.File.Spans.Select(s => Describe(p.File.Text, s)))}").Order(StringComparer.Ordinal);
        var actual = result.Edits
            .Select(e => $"{Path.GetFileName(e.Path)}: {string.Join(" ", e.Changes.Select(c => Describe(files.Single(f => TestFiles.PathOf(f.Name) == e.Path).Text, c.Span)))}")
            .Order(StringComparer.Ordinal);
        Assert.Equal(expected, actual);
        Assert.All(result.Edits.SelectMany(e => e.Changes), c => Assert.Equal(newName, c.NewText));

        // the renamed project: no error, and every renamed name binds to a symbol of the new name - or, a type parameter in the body
        // of a generic's instance (C-421), to the same argument as before
        var after = files.ToDictionary(f => TestFiles.PathOf(f.Name), f => f.Text, StringComparer.OrdinalIgnoreCase);
        var renamed = new List<(string Path, int Start, int OldStart, string OldName)>();
        foreach (var edit in result.Edits)
        {
            var shift = 0;
            foreach (var change in edit.Changes.OrderBy(c => c.Span.Start))
            {
                renamed.Add((edit.Path, change.Span.Start + shift, change.Span.Start, after[edit.Path].Substring(change.Span.Start, change.Span.Length)));
                shift += change.NewText.Length - change.Span.Length;
            }

            after[edit.Path] = Apply(after[edit.Path], edit.Changes);
        }

        var workspace = new Workspace(TestFiles.Files([.. after.Select(f => (Path.GetFileName(f.Key), f.Value))]));
        workspace.SetProject(TestFiles.Options());
        Assert.Empty(workspace.CurrentCompilation.GetDiagnostics(null, ct).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToText()));
        var original = new Workspace(TestFiles.Files(files));
        original.SetProject(TestFiles.Options());
        foreach (var (path, start, oldStart, oldName) in renamed)
        {
            var model = workspace.GetDocument(path).SemanticModel;
            var token = model.SyntaxTree.FindToken(start);
            var symbol = QuickInfoService.SymbolOf(model, token);
            var oldModel = original.GetDocument(path).SemanticModel;
            var was = QuickInfoService.SymbolOf(oldModel, oldModel.SyntaxTree.FindToken(oldStart));
            var asBefore = was is not null && !string.Equals(was.Name, oldName, StringComparison.OrdinalIgnoreCase) && SymbolKey.AreSame(was, symbol);
            Assert.True(symbol is not null && (string.Equals(symbol.Name, newName, StringComparison.OrdinalIgnoreCase) || asBefore),
                $"{Path.GetFileName(path)}: '{token.Text}' at {start} binds to {symbol?.Name ?? "nothing"}");
        }
    }

    private static string Describe(string text, TextSpan span)
    {
        var line = text[..span.Start].Count(c => c == '\n') + 1;
        var column = span.Start - (text.LastIndexOf('\n', Math.Max(0, span.Start - 1)) + 1) + 1;
        return $"{line}:{column}:{text.Substring(span.Start, span.Length)}";
    }

    private static string Apply(string text, IEnumerable<TextChange> changes)
    {
        var builder = new System.Text.StringBuilder(text);
        foreach (var change in changes.OrderByDescending(c => c.Span.Start))
        {
            builder.Remove(change.Span.Start, change.Span.Length).Insert(change.Span.Start, change.NewText);
        }

        return builder.ToString();
    }

    [Fact]
    public void Parameter_BothHeadingsAndTheBody()
    {
        AssertRename("U.pas", "Amount",
            ("P.pas", "program P;\nuses U;\nbegin\n  Add(1);\nend.\n"),
            ("U.pas", """
                unit U;
                interface
                procedure Add([|Value|]: Integer);
                implementation
                var Total: Integer;
                procedure Add([|$$Value|]: Integer);
                begin
                  Total := Total + [|Value|];
                  if [|Value|] > 0 then Total := [|Value|] * 2;
                end;
                end.
                """));
    }

    [Fact]
    public void NestedRoutine_HeadingAndCalls()
    {
        AssertRename("P.pas", "Twice",
            ("P.pas", """
                program P;
                procedure Outer;
                var N: Integer;
                  function [|Dbl|](X: Integer): Integer; forward;
                  procedure Show;
                  begin
                    N := [|Dbl|](N);
                  end;
                  function [|$$Dbl|](X: Integer): Integer;
                  begin
                    Result := X * 2;
                  end;
                begin
                  N := [|Dbl|](3);
                  Show;
                end;
                begin
                  Outer;
                end.
                """));
    }

    [Fact]
    public void Label_NameAndNumber()
    {
        AssertRename("P.pas", "Done",
            ("P.pas", """
                program P;
                procedure Run;
                label [|Again|], 10;
                var I: Integer;
                begin
                  I := 0;
                [|Again|]:
                  I := I + 1;
                  if I < 3 then goto [|$$Again|];
                  goto 10;
                10:
                end;
                begin
                  Run;
                end.
                """));
        AssertRename("P.pas", "20",
            ("P.pas", """
                program P;
                procedure Run;
                label [|10|];
                begin
                  goto [|$$10|];
                [|10|]:
                end;
                begin
                  Run;
                end.
                """));
    }

    [Fact]
    public void UnitLevelRoutine_BothHeadingsCallsAndQualifiedCalls()
    {
        AssertRename("U.pas", "Greet",
            ("P.pas", "program P;\nuses U;\nbegin\n  [|Hello|];\n  U.[|Hello|];\nend.\n"),
            ("U.pas", """
                unit U;
                interface
                procedure [|$$Hello|];
                implementation
                procedure [|Hello|];
                begin
                end;
                procedure Twice;
                begin
                  [|Hello|];
                  U.[|Hello|];
                end;
                end.
                """));
    }

    [Fact]
    public void ClassType_ForwardDeclarationReferencesAndHeadingQualifiers()
    {
        AssertRename("U.pas", "TItem",
            ("P.pas", "program P;\nuses U;\nvar N: [|TNode|];\nbegin\n  N := [|TNode|].Create;\n  N.Free;\nend.\n"),
            ("U.pas", """
                unit U;
                interface
                type
                  [|TNode|] = class;
                  TNodeClass = class of [|TNode|];
                  [|$$TNode|] = class
                    Next: [|TNode|];
                    constructor Create;
                    function Last: [|TNode|];
                  end;
                implementation
                constructor [|TNode|].Create;
                begin
                  Next := nil;
                end;
                function [|TNode|].Last: [|TNode|];
                begin
                  Result := Self;
                  while Result.Next <> nil do Result := Result.Next;
                end;
                end.
                """));
    }

    [Fact]
    public void RecordAndClassField_AlsoInPropertySpecifiers()
    {
        AssertRename("P.pas", "FSize",
            ("P.pas", """
                program P;
                type
                  TBox = class
                  private
                    [|$$FCount|]: Integer;
                    function Stored: Boolean;
                  public
                    property Count: Integer read [|FCount|] write [|FCount|] stored Stored;
                    procedure Bump;
                  end;
                function TBox.Stored: Boolean; begin Result := [|FCount|] > 0 end;
                procedure TBox.Bump; begin Inc([|FCount|]) end;
                var B: TBox;
                begin
                  B := TBox.Create;
                  B.Bump;
                  B.Free;
                end.
                """));
        AssertRename("P.pas", "Y",
            ("P.pas", """
                program P;
                type
                  TPoint = record
                    X, [|$$Z|]: Integer;
                    property Height: Integer read [|Z|] write [|Z|];
                  end;
                var Pt: TPoint;
                begin
                  Pt.[|Z|] := 1;
                  with Pt do [|Z|] := [|Z|] + X;
                end.
                """));
    }

    [Fact]
    public void Method_ResolutionClauseAndPropertySpecifiers()
    {
        AssertRename("P.pas", "Fetch",
            ("P.pas", """
                program P;
                type
                  IReader = interface
                    function Read: Integer;
                  end;
                  TReader = class(TInterfacedObject, IReader)
                    function IReader.Read = [|$$GetValue|];
                    function [|GetValue|]: Integer;
                    property Value: Integer read [|GetValue|];
                  end;
                function TReader.[|GetValue|]: Integer; begin Result := 1 end;
                var R: TReader;
                begin
                  R := TReader.Create;
                  if R.[|GetValue|] = R.Value then R.Free;
                end.
                """));
    }

    [Fact]
    public void ConstructorAndDestructor()
    {
        AssertRename("P.pas", "Make",
            ("P.pas", """
                program P;
                type
                  TRes = class
                    constructor [|$$Open|](N: Integer);
                    destructor Close;
                  end;
                constructor TRes.[|Open|](N: Integer); begin inherited Create end;
                destructor TRes.Close; begin inherited Destroy end;
                var R: TRes;
                begin
                  R := TRes.[|Open|](1);
                  R.Close;
                end.
                """));
        AssertRename("P.pas", "Shut",
            ("P.pas", """
                program P;
                type
                  TRes = class
                    constructor Open(N: Integer);
                    destructor [|Close|];
                  end;
                constructor TRes.Open(N: Integer); begin inherited Create end;
                destructor TRes.[|$$Close|]; begin inherited Destroy end;
                var R: TRes;
                begin
                  R := TRes.Open(1);
                  R.[|Close|];
                end.
                """));
    }

    [Fact]
    public void Property_RedeclarationsButNotItsAccessors()
    {
        AssertRename("P.pas", "Size",
            ("P.pas", """
                program P;
                type
                  TBase = class
                  private
                    FCount: Integer;
                    function GetCount: Integer;
                  public
                    property [|$$Count|]: Integer read GetCount write FCount;
                  end;
                  TDerived = class(TBase)
                  public
                    property [|Count|];
                  end;
                function TBase.GetCount: Integer; begin Result := FCount end;
                var D: TDerived;
                begin
                  D := TDerived.Create;
                  D.[|Count|] := D.[|Count|] + 1;
                  D.Free;
                end.
                """));
    }

    [Fact]
    public void InterfaceProperty()
    {
        AssertRename("P.pas", "Caption",
            ("P.pas", """
                program P;
                type
                  INamed = interface
                    function GetName: string;
                    property [|$$Name|]: string read GetName;
                  end;
                  TThing = class(TInterfacedObject, INamed)
                    function GetName: string;
                  end;
                function TThing.GetName: string; begin Result := 'x' end;
                var N: INamed;
                begin
                  N := TThing.Create;
                  if N.[|Name|] = '' then N := nil;
                end.
                """));
    }

    [Fact]
    public void EnumerationValue_ScopedAndUnscopedUses()
    {
        AssertRename("P.pas", "Amber",
            ("P.pas", """
                program P;
                type
                  TLight = (Red, [|$$Yellow|], Green);
                var L: TLight;
                begin
                  L := [|Yellow|];
                  if L = TLight.[|Yellow|] then L := Green;
                  case L of
                    [|Yellow|]: L := Red;
                  end;
                end.
                """));
        AssertRename("P.pas", "Amber",
            ("P.pas", """
                program P;
                type
                  {$SCOPEDENUMS ON}
                  TLight = (Red, [|$$Yellow|], Green);
                  {$SCOPEDENUMS OFF}
                var L: TLight;
                begin
                  L := TLight.[|Yellow|];
                  if L = TLight.[|Yellow|] then L := TLight.Green;
                end.
                """));
    }

    [Fact]
    public void TypeParameterOfAGenericType_DeclarationHeadingsAndUses()
    {
        AssertRename("P.pas", "TItem",
            ("P.pas", """
                program P;
                type
                  TBox<[|$$T|]> = class
                    Value: [|T|];
                    function Get: [|T|];
                    procedure Put(const V: [|T|]);
                  end;
                function TBox<[|T|]>.Get: [|T|];
                begin
                  Result := Value;
                end;
                procedure TBox<[|T|]>.Put(const V: [|T|]);
                var Old: [|T|];
                begin
                  Old := Value;
                  Value := V;
                end;
                var B: TBox<Integer>;
                begin
                  B := TBox<Integer>.Create;
                  B.Put(1);
                  B.Free;
                end.
                """));
    }

    [Fact]
    public void TypeParameterOfAGenericRoutine_IsLocal()
    {
        const string Markup = """
            program P;
            type
              TUtil = class
                class function Pick<[|$$T|]>(A, B: [|T|]; First: Boolean): [|T|];
              end;
            class function TUtil.Pick<[|T|]>(A, B: [|T|]; First: Boolean): [|T|];
            var Chosen: [|T|];
            begin
              if First then Chosen := A else Chosen := B;
              Result := Chosen;
            end;
            begin
              WriteLn(TUtil.Pick<Integer>(1, 2, True));
            end.
            """;
        AssertRename("P.pas", "TValue", ("P.pas", Markup));

        var file = MarkupTestFile.Parse(Markup);
        var info = RenameService.GetRenameInfo(MarkupTestFile.Open("P.pas", ("P.pas", file.Text)), file.Caret, TestContext.Current.CancellationToken);
        Assert.Equal(RenameScope.Local, info.Scope);
        Assert.Equal(file.Spans, info.LocalSpans);
    }

    [Fact]
    public void TypeParameter_NewNameUsedInItsScope_IsAConflict()
    {
        var file = MarkupTestFile.Parse("""
            program P;
            type
              TPair<$$T> = class
                Count: Integer;
                Value: T;
              end;
            var B: TPair<string>;
            begin
              B := nil;
            end.
            """);
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", file.Text));
        var ct = TestContext.Current.CancellationToken;

        var info = RenameService.GetRenameInfo(doc, file.Caret, ct);
        var result = RenameService.Rename(doc, file.Caret, "Integer", ct);

        Assert.True(info.CanRename, info.Reason);
        Assert.Equal((CompletionItemKind.Type, "TPair<T>", RenameScope.Project), (info.Kind, info.ContainerName, info.Scope));
        Assert.False(result.Success);
        Assert.Equal("Renaming 'T' to 'Integer' changes the meaning of 'Integer' at P.pas(4,12).", result.Error);
        Assert.Empty(result.Edits);
    }
}
