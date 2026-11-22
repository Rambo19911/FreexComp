using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// api.md §4.2, the code-sync edits of C-152 (CMP-250…259). Every edit case applies the changes, re-parses (no new syntax
/// diagnostics, round trip, CMP-259) and compares the exact resulting text, so the untouched text is byte-identical.
/// </summary>
public sealed partial class SyntaxEditsTests
{
    /// <summary>The unit shape of code-sync.md §2, with a comment, a private field and a multi-name field added.</summary>
    private const string Template = """
        unit MainForm;

        interface

        uses
          Classes, Controls, Forms, StdCtrls;

        type
          TMainForm = class(TForm)
            NameEdit: TEdit;
            GreetButton: TButton; // the button
            procedure GreetButtonClick(Sender: TObject);
          private
            FCount: Integer;
          public
            A, B, C: TLabel;
          end;

        var
          MainForm: TMainForm;

        implementation

        {$FORM MainForm.form}

        procedure TMainForm.GreetButtonClick(Sender: TObject);
        begin

        end;

        end.
        """;

    private const string EmptyForm = """
        unit MainForm;

        interface

        type
          TMainForm = class(TForm)
          private
          public
          end;

        implementation

        end.
        """;

    private static ClassTypeSyntax Class(SyntaxTree tree, string name) => (ClassTypeSyntax)Type(tree, name).Type;

    // ------------------------------------------------------------------ locating

    [Fact]
    public void CodeSync_FindClass_FirstClassWithBodyInTheInterface()
    {
        var source = """
            unit U;
            interface
            type
              TFoo = class;
              TRef = class of TFoo;
              TFoo = class
                X: Integer;
              end;
            implementation
            type
              TImpl = class
              end;
            end.
            """;
        var tree = Parse(source);

        var found = SyntaxEdits.FindClass(tree.Root, "tfoo");

        Assert.NotNull(found);
        Assert.NotNull(((ClassTypeSyntax)found.Type).EndKeyword);
        Assert.Null(SyntaxEdits.FindClass(tree.Root, "TRef"));
        Assert.Null(SyntaxEdits.FindClass(tree.Root, "TImpl"));
        Assert.Equal("TImpl", SyntaxEdits.FindClass(tree.Root, "TImpl", interfaceSectionOnly: false)?.Identifier.Text);
        Assert.Null(SyntaxEdits.FindClass(tree.Root, "TMissing", interfaceSectionOnly: false));

        var program = Parse("program P;\r\ntype\r\n  TBar = class\r\n  end;\r\nbegin\r\nend.\r\n");
        Assert.Equal("TBar", SyntaxEdits.FindClass(program.Root, "TBar")?.Identifier.Text);
    }

    [Fact]
    public void CodeSync_GetDefaultSectionMembers_StopsAtTheFirstVisibilitySpecifier()
    {
        var tree = Parse(Template);

        var members = SyntaxEdits.GetDefaultSectionMembers(Class(tree, "TMainForm"));

        Assert.Equal(["NameEdit: TEdit;", "GreetButton: TButton;", "procedure GreetButtonClick(Sender: TObject);"],
            members.Select(m => m.ToString()));
        Assert.Empty(SyntaxEdits.GetDefaultSectionMembers(Class(Parse(EmptyForm), "TMainForm")));
    }

    [Fact]
    public void CodeSync_FindMember_SearchesEverySectionAndMultiNameFields()
    {
        var tree = Parse(Template.Replace("    FCount: Integer;", "    FCount: Integer;\r\n    property Count: Integer read FCount;"));
        var cls = Class(tree, "TMainForm");

        Assert.IsType<FieldDeclarationSyntax>(SyntaxEdits.FindMember(cls, "fcount"));
        Assert.Equal("A, B, C: TLabel;", SyntaxEdits.FindMember(cls, "B")?.ToString());
        Assert.IsType<MethodDeclarationSyntax>(SyntaxEdits.FindMember(cls, "GREETBUTTONCLICK"));
        Assert.IsType<PropertyDeclarationSyntax>(SyntaxEdits.FindMember(cls, "Count"));
        Assert.Null(SyntaxEdits.FindMember(cls, "Missing"));
    }

    [Fact]
    public void CodeSync_FindMethodImplementation_AndIsEmptyMethodBody()
    {
        var source = """
            unit U;
            interface
            type
              TFoo = class
                procedure A; procedure B; procedure C; procedure D; procedure E; procedure F;
              end;
            implementation
            procedure TFoo.A;
            begin

            end;
            procedure TFoo.B; begin end;
            procedure TFoo.C;
            begin
              Beep;
            end;
            procedure TFoo.D;
            begin
              // keep me
            end;
            procedure TFoo.E;
            var
              X: Integer;
            begin
            end;
            procedure F;
            begin
            end;
            end.
            """;
        var unit = Unit(Parse(source));

        bool Empty(string name) => SyntaxEdits.IsEmptyMethodBody(SyntaxEdits.FindMethodImplementation(unit, "tfoo", name)!);

        Assert.True(Empty("a"));
        Assert.True(Empty("B"));
        Assert.False(Empty("C"));
        Assert.False(Empty("D"));
        Assert.False(Empty("E"));
        Assert.Null(SyntaxEdits.FindMethodImplementation(unit, "TFoo", "F"));       // not qualified with the class
        Assert.Null(SyntaxEdits.FindMethodImplementation(unit, "TBar", "A"));
    }

    [Fact]
    public void CodeSync_FindDirective_InTheScopeOnly()
    {
        var tree = Parse(Template);

        var form = SyntaxEdits.FindDirective(tree.Root, "form");

        Assert.NotNull(form);
        Assert.Equal("MainForm.form", form.Argument);
        Assert.Equal("{$FORM MainForm.form}", tree.Text.Text.Substring(form.Span.Start, form.Span.Length));
        Assert.Equal(form, SyntaxEdits.FindDirective(Unit(tree).ImplementationSection, "FORM"));
        Assert.Null(SyntaxEdits.FindDirective(Unit(tree).InterfaceSection, "FORM"));
        Assert.Null(SyntaxEdits.FindDirective(tree.Root, "R"));
    }

    // ------------------------------------------------------------------ AddField (CMP-250)

    [Fact]
    public void CodeSync_AddField_AfterTheLastFieldOfTheDefaultSection_CMP250()
    {
        var tree = Parse(Template);

        var text = Apply(tree, SyntaxEdits.AddField(Type(tree, "TMainForm"), "OkButton", "TButton"));

        Assert.Equal(Expected(Template).Replace("    GreetButton: TButton; // the button\r\n",
            "    GreetButton: TButton; // the button\r\n    OkButton: TButton;\r\n"), text);
    }

    [Fact]
    public void CodeSync_AddField_NoMembers_AfterTheHeadingWithHeadingIndentPlusIndentUnit_CMP250()
    {
        var tree = Parse(EmptyForm);

        var text = Apply(tree, SyntaxEdits.AddField(Type(tree, "TMainForm"), "Button1", "TButton"));
        var tabbed = Apply(tree, SyntaxEdits.AddField(Type(tree, "TMainForm"), "Button1", "TButton", new EditOptions(IndentUnit: "\t")));

        Assert.Equal(Expected(EmptyForm).Replace("class(TForm)\r\n", "class(TForm)\r\n    Button1: TButton;\r\n"), text);
        Assert.Equal(Expected(EmptyForm).Replace("class(TForm)\r\n", "class(TForm)\r\n  \tButton1: TButton;\r\n"), tabbed);
    }

    [Fact]
    public void CodeSync_AddField_NoDefaultField_IndentOfTheFirstMemberLine_CMP250()
    {
        var source = """
            unit U;
            interface
            type
              TFoo = class
                  procedure Run;
              private
                FA: Integer;
              end;
            implementation
            end.
            """;
        var tree = Parse(source);

        var text = Apply(tree, SyntaxEdits.AddField(Type(tree, "TFoo"), "Edit1", "TEdit"));

        Assert.Equal(Expected(source).Replace("  TFoo = class\r\n", "  TFoo = class\r\n      Edit1: TEdit;\r\n"), text);
    }

    [Fact]
    public void CodeSync_AddField_KeepsLfLineEndingsTabsAndComments_CMP250()
    {
        var source = "unit U;\n{ head }\ninterface\ntype\n\tTFoo = class\n\t\tA: Integer;\t{ a }\n\tend;\nimplementation\nend.\n";
        var tree = SyntaxTree.Parse(SourceText.From(Path, source));

        var text = Apply(tree, SyntaxEdits.AddField(Type(tree, "TFoo"), "B", "Byte", new EditOptions(LineEnding: "\n", IndentUnit: "\t")));

        Assert.Equal(source.Replace("\t\tA: Integer;\t{ a }\n", "\t\tA: Integer;\t{ a }\n\t\tB: Byte;\n"), text);
    }

    // ------------------------------------------------------------------ RemoveField (CMP-251)

    [Fact]
    public void CodeSync_RemoveField_SingleName_WholeLineWithTrailingComment_CMP251()
    {
        var tree = Parse(Template);

        var changes = SyntaxEdits.RemoveField(Type(tree, "TMainForm"), "greetbutton");

        Assert.Single(changes);
        Assert.Equal(Expected(Template).Replace("    GreetButton: TButton; // the button\r\n", ""), Apply(tree, [.. changes]));
        // other sections are searched too
        Assert.Equal(Expected(Template).Replace("    FCount: Integer;\r\n", ""),
            Apply(tree, [.. SyntaxEdits.RemoveField(Type(tree, "TMainForm"), "FCount")]));
    }

    [Fact]
    public void CodeSync_RemoveField_MultiName_OnlyTheNameAndOneComma_CMP251()
    {
        var tree = Parse(Template);
        string Remove(string name) => Apply(tree, [.. SyntaxEdits.RemoveField(Type(tree, "TMainForm"), name)]);

        Assert.Equal(Expected(Template).Replace("A, B, C: TLabel;", "B, C: TLabel;"), Remove("A"));
        Assert.Equal(Expected(Template).Replace("A, B, C: TLabel;", "A, C: TLabel;"), Remove("B"));
        Assert.Equal(Expected(Template).Replace("A, B, C: TLabel;", "A, B: TLabel;"), Remove("C"));
    }

    [Fact]
    public void CodeSync_RemoveField_NotFound_IsEmpty_CMP251()
    {
        var tree = Parse(Template);

        Assert.Empty(SyntaxEdits.RemoveField(Type(tree, "TMainForm"), "Missing"));
        Assert.Empty(SyntaxEdits.RemoveField(Type(tree, "TMainForm"), "GreetButtonClick"));     // a method, not a field
    }

    // ------------------------------------------------------------------ RenameField (CMP-252)

    [Fact]
    public void CodeSync_RenameField_OnlyTheDeclarationIdentifier_CMP252()
    {
        var source = """
            unit U;
            interface
            type
              TFoo = class
                Edit1: TEdit;
                procedure Run;
              end;
            implementation
            procedure TFoo.Run;
            begin
              Edit1.Text := 'Edit1';
            end;
            end.
            """;
        var tree = Parse(source);

        var change = SyntaxEdits.RenameField(Type(tree, "TFoo"), "EDIT1", "NameEdit");

        Assert.Equal(Expected(source).Replace("    Edit1: TEdit;", "    NameEdit: TEdit;"), Apply(tree, change));
        Assert.True(SyntaxEdits.RenameField(Type(tree, "TFoo"), "Run", "Go").IsEmpty);
        Assert.True(SyntaxEdits.RenameField(Type(tree, "TFoo"), "Missing", "Go").IsEmpty);
    }

    [Fact]
    public void CodeSync_RenameField_InsideAMultiNameDeclaration_CMP252()
    {
        var tree = Parse(Template);

        var text = Apply(tree, SyntaxEdits.RenameField(Type(tree, "TMainForm"), "b", "Title"));

        Assert.Equal(Expected(Template).Replace("A, B, C: TLabel;", "A, Title, C: TLabel;"), text);
    }

    // ------------------------------------------------------------------ AddMethodDeclaration (CMP-253)

    [Fact]
    public void CodeSync_AddMethodDeclaration_AfterTheLastMethodOfTheDefaultSection_CMP253()
    {
        var tree = Parse(Template);

        var text = Apply(tree, SyntaxEdits.AddMethodDeclaration(Type(tree, "TMainForm"), "procedure NameEditChange(Sender: TObject);"));

        Assert.Equal(Expected(Template).Replace("    procedure GreetButtonClick(Sender: TObject);\r\n",
            "    procedure GreetButtonClick(Sender: TObject);\r\n    procedure NameEditChange(Sender: TObject);\r\n"), text);
    }

    [Fact]
    public void CodeSync_AddMethodDeclaration_NoMethod_AfterTheLastField_CMP253()
    {
        var source = """
            unit U;
            interface
            type
              TFoo = class
                Edit1: TEdit;
                Edit2: TEdit;
              private
                procedure Hidden;
              end;
            implementation
            end.
            """;
        var tree = Parse(source);

        var text = Apply(tree, SyntaxEdits.AddMethodDeclaration(Type(tree, "TFoo"), "function Check: Boolean;"));

        Assert.Equal(Expected(source).Replace("    Edit2: TEdit;\r\n", "    Edit2: TEdit;\r\n    function Check: Boolean;\r\n"), text);
    }

    [Fact]
    public void CodeSync_AddMethodDeclaration_EmptySection_AtTheStartLikeAddField_CMP253()
    {
        var tree = Parse(EmptyForm);

        var text = Apply(tree, SyntaxEdits.AddMethodDeclaration(Type(tree, "TMainForm"), "procedure FormCreate(Sender: TObject);"));

        Assert.Equal(Expected(EmptyForm).Replace("class(TForm)\r\n", "class(TForm)\r\n    procedure FormCreate(Sender: TObject);\r\n"), text);
    }

    // ------------------------------------------------------------------ InsertMethodImplementation (CMP-254)

    [Fact]
    public void CodeSync_InsertMethodImplementation_HandlerStubAfterTheLastHandler_CMP254()
    {
        var tree = Parse(Template);
        const string stub = "procedure TMainForm.NameEditChange(Sender: TObject);\r\nbegin\r\n  \r\nend;";

        var text = Apply(tree, SyntaxEdits.InsertMethodImplementation(Unit(tree), "TMainForm", "\r\n" + stub + "\r\n"));

        Assert.Equal(Expected(Template).Replace("end;\r\n\r\nend.", "end;\r\n\r\n" + stub + "\r\n\r\nend."), text);
    }

    [Fact]
    public void CodeSync_InsertMethodImplementation_FirstHandlerAfterTheFormDirective_CMP254()
    {
        var source = Expected(EmptyForm).Replace("implementation\r\n\r\n", "implementation\r\n\r\n{$FORM MainForm.form}\r\n\r\n");
        var tree = Parse(source);
        const string stub = "procedure TMainForm.FormCreate(Sender: TObject);\r\nbegin\r\n  \r\nend;";

        var text = Apply(tree, SyntaxEdits.InsertMethodImplementation(Unit(tree), "TMainForm", stub));

        Assert.Equal(source.Replace("{$FORM MainForm.form}\r\n\r\nend.", "{$FORM MainForm.form}\r\n\r\n" + stub + "\r\n\r\nend."), text);
    }

    // ------------------------------------------------------------------ RenameMethod (CMP-255)

    [Fact]
    public void CodeSync_RenameMethod_DeclarationAndHeadingOnly_CMP255()
    {
        var source = Expected(Template).Replace("begin\r\n\r\nend;", "begin\r\n  GreetButtonClick(nil);\r\nend;");
        var tree = Parse(source);

        var changes = SyntaxEdits.RenameMethod(Unit(tree), "TMainForm", "GreetButtonClick", "SayHello");

        Assert.Equal(2, changes.Length);
        Assert.Equal(source.Replace("procedure GreetButtonClick(", "procedure SayHello(")
            .Replace("TMainForm.GreetButtonClick(", "TMainForm.SayHello("), Apply(tree, [.. changes]));
    }

    // ------------------------------------------------------------------ RemoveMethod (CMP-256)

    [Fact]
    public void CodeSync_RemoveMethod_DeclarationAndImplementationWithTheFollowingBlankLine_CMP256()
    {
        var tree = Parse(Template);

        var changes = SyntaxEdits.RemoveMethod(Unit(tree), "TMainForm", "greetbuttonclick");

        Assert.Equal(2, changes.Length);
        Assert.Equal(Expected(Template)
                .Replace("    procedure GreetButtonClick(Sender: TObject);\r\n", "")
                .Replace("procedure TMainForm.GreetButtonClick(Sender: TObject);\r\nbegin\r\n\r\nend;\r\n\r\n", ""),
            Apply(tree, [.. changes]));
    }

    [Fact]
    public void CodeSync_RemoveMethod_NoFollowingBlankLine_RemovesThePrecedingOne_CMP256()
    {
        var source = """
            unit U;
            interface
            type
              TFoo = class
                procedure A;
                procedure B;
              end;
            implementation

            procedure TFoo.A;
            begin
            end;
            procedure TFoo.B;
            begin
            end;
            end.
            """;
        var tree = Parse(source);

        var removeA = Apply(tree, [.. SyntaxEdits.RemoveMethod(Unit(tree), "TFoo", "A")]);
        var removeB = Apply(tree, [.. SyntaxEdits.RemoveMethod(Unit(tree), "TFoo", "B")]);

        Assert.Equal(Expected(source).Replace("    procedure A;\r\n", "").Replace("\r\nprocedure TFoo.A;\r\nbegin\r\nend;\r\n", ""), removeA);
        Assert.Equal(Expected(source).Replace("    procedure B;\r\n", "").Replace("procedure TFoo.B;\r\nbegin\r\nend;\r\n", ""), removeB);
    }

    [Fact]
    public void CodeSync_RemoveMethod_DeclarationOnly_OrUnknown_CMP256()
    {
        var tree = Parse("unit U;\r\ninterface\r\ntype\r\n  TFoo = class\r\n    procedure A;\r\n  end;\r\nimplementation\r\nend.\r\n");

        Assert.Equal("unit U;\r\ninterface\r\ntype\r\n  TFoo = class\r\n  end;\r\nimplementation\r\nend.\r\n",
            Apply(tree, [.. SyntaxEdits.RemoveMethod(Unit(tree), "TFoo", "A")]));
        Assert.Empty(SyntaxEdits.RemoveMethod(Unit(tree), "TFoo", "Missing"));
        Assert.Empty(SyntaxEdits.RemoveMethod(Unit(tree), "TBar", "A"));
    }

    // ------------------------------------------------------------------ UsesContains (CMP-257)

    [Fact]
    public void CodeSync_UsesContains_FullDottedNameCaseInsensitivePerSection_CMP257()
    {
        var tree = Parse("unit U; interface uses System.Classes, Forms; implementation uses Dialogs; end.");

        Assert.True(SyntaxEdits.UsesContains(tree.Root, "forms", interfaceSection: true));
        Assert.True(SyntaxEdits.UsesContains(tree.Root, "system.classes", interfaceSection: true));
        Assert.False(SyntaxEdits.UsesContains(tree.Root, "Classes", interfaceSection: true));
        Assert.False(SyntaxEdits.UsesContains(tree.Root, "Dialogs", interfaceSection: true));
        Assert.True(SyntaxEdits.UsesContains(tree.Root, "Dialogs", interfaceSection: false));
        Assert.False(SyntaxEdits.UsesContains(Parse(EmptyForm).Root, "Forms", interfaceSection: true));

        // the designer's flow: present only in the implementation → added to the interface clause (DSG-CS-051)
        Assert.Equal("unit U; interface uses System.Classes, Forms, Dialogs; implementation uses Dialogs; end.",
            Apply(tree, SyntaxEdits.AddUsesEntry(tree.Root, "Dialogs", interfaceSection: true)));
    }

    // ------------------------------------------------------------------ RenameClass (CMP-258)

    [Fact]
    public void CodeSync_RenameClass_DeclarationQualifiersAndInterfaceVariables_CMP258()
    {
        var tree = Parse(Template);

        var changes = SyntaxEdits.RenameClass(Unit(tree), "tmainform", "TStartForm");

        Assert.Equal(3, changes.Length);
        Assert.Equal(Expected(Template).Replace("  TMainForm = class(TForm)", "  TStartForm = class(TForm)")
            .Replace("MainForm: TMainForm;", "MainForm: TStartForm;")
            .Replace("procedure TMainForm.", "procedure TStartForm."), Apply(tree, [.. changes]));
    }

    [Fact]
    public void CodeSync_RenameClass_NothingElse_CMP258()
    {
        var source = """
            unit TFoo;
            interface
            type
              TFoo = class
                procedure Run;
              end;
              TOther = class(TFoo)
              end;
            var
              One, Two: TFoo;
              List: array of TFoo;
            implementation
            var
              Hidden: TFoo;
            procedure TFoo.Run;
            var
              Local: TFoo;
            begin
              Local := TFoo(Self);
            end;
            procedure TOther.Run;
            begin
            end;
            end.
            """;
        var tree = Parse(source);

        var text = Apply(tree, [.. SyntaxEdits.RenameClass(Unit(tree), "TFoo", "TBar")]);

        Assert.Equal(Expected(source).Replace("  TFoo = class\r\n", "  TBar = class\r\n")
            .Replace("One, Two: TFoo;", "One, Two: TBar;")
            .Replace("procedure TFoo.Run;", "procedure TBar.Run;"), text);
        Assert.Empty(SyntaxEdits.RenameClass(Unit(tree), "TMissing", "TBar"));
    }

    [Fact]
    public void CodeSync_RenameClassRenamesForwardDeclaration_CMP258()
    {
        // C-267: the forward declaration `TOld = class;` is renamed too (in the interface or the implementation section);
        // the `class of` reference and other type references stay (CMP-258 "nothing else")
        var source = """
            unit Nodes;
            interface
            type
              TNode = class;
              TOther = class;
              TNodeClass = class of TNode;
              TNode = class
                Next: TNode;
                procedure Run;
              end;
              TOther = class
              end;
            var
              Root: TNode;
            implementation
            type
              tnode = class;
            procedure TNode.Run;
            begin
            end;
            end.
            """;
        var tree = Parse(source);

        var text = Apply(tree, [.. SyntaxEdits.RenameClass(Unit(tree), "TNode", "TItem")]);

        Assert.Equal(Expected(source).Replace("  TNode = class;", "  TItem = class;")
            .Replace("  TNode = class\r\n", "  TItem = class\r\n")
            .Replace("Root: TNode;", "Root: TItem;")
            .Replace("  tnode = class;", "  TItem = class;")
            .Replace("procedure TNode.Run;", "procedure TItem.Run;"), text);

        // a class declared in the implementation section, with its forward declaration there
        var hidden = """
            unit Hidden;
            interface
            implementation
            type
              THidden = class;
              THidden = class
                procedure Run;
              end;
            procedure THidden.Run;
            begin
            end;
            end.
            """;
        tree = Parse(hidden);
        Assert.Equal(Expected(hidden).Replace("THidden", "TSecret"),
            Apply(tree, [.. SyntaxEdits.RenameClass(Unit(tree), "THidden", "TSecret")]));

        // the renamed unit still binds without errors (no other type references: CMP-258 leaves those alone)
        var bindable = """
            unit Nodes;
            interface
            type
              TNode = class;
              TNode = class
                procedure Run;
              end;
            var
              Root: TNode;
            implementation
            procedure TNode.Run;
            begin
            end;
            end.
            """;
        tree = Parse(bindable);
        var renamed = Apply(tree, [.. SyntaxEdits.RenameClass(Unit(tree), "TNode", "TItem")]);
        Assert.Empty(BindErrors(bindable));
        Assert.Empty(BindErrors(renamed));
        Assert.NotEmpty(BindErrors(renamed.Replace("  TItem = class;", "  TNode = class;")));   // the C-152 result
    }

    /// <summary>The error diagnostics of unit <c>Nodes</c> used by a program, bound against the test RTL.</summary>
    private static IEnumerable<string> BindErrors(string unitText)
    {
        var unitPath = @"C:\p\Nodes.pas";
        var fs = new InMemoryFileSystem().AddSystem()
            .Add(@"C:\p\P.pas", "program P; uses Nodes; begin end.")
            .Add(unitPath, unitText);
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        return Compilation.Create(options, [], fs).GetSemanticModel(unitPath).GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToText()).ToList();
    }

    // ------------------------------------------------------------------ CMP-259

    [Fact]
    public void EditReparseRoundTrip()
    {
        var tree = Parse(EmptyForm);
        var steps = new Func<SyntaxTree, TextChange[]>[]
        {
            t => [SyntaxEdits.AddUsesEntry(t.Root, "Forms", interfaceSection: true)],
            t => [SyntaxEdits.AddField(Type(t, "TMainForm"), "Button1", "TButton")],
            t => [SyntaxEdits.AddField(Type(t, "TMainForm"), "Edit1", "TEdit")],
            t => [SyntaxEdits.AddMethodDeclaration(Type(t, "TMainForm"), "procedure Button1Click(Sender: TObject);")],
            t => [SyntaxEdits.InsertMethodImplementation(Unit(t), "TMainForm",
                "procedure TMainForm.Button1Click(Sender: TObject);\r\nbegin\r\n  \r\nend;")],
            t => [SyntaxEdits.AddMethodDeclaration(Type(t, "TMainForm"), "procedure Edit1Change(Sender: TObject);")],
            t => [SyntaxEdits.InsertMethodImplementation(Unit(t), "TMainForm",
                "procedure TMainForm.Edit1Change(Sender: TObject);\r\nbegin\r\n  \r\nend;")],
            t => [SyntaxEdits.RenameField(Type(t, "TMainForm"), "Button1", "OkButton")],
            t => [.. SyntaxEdits.RenameMethod(Unit(t), "TMainForm", "Button1Click", "OkButtonClick")],
            t => [.. SyntaxEdits.RenameClass(Unit(t), "TMainForm", "TStartForm")],
            t => [.. SyntaxEdits.RemoveField(Type(t, "TStartForm"), "Edit1")],
            t => [.. SyntaxEdits.RemoveMethod(Unit(t), "TStartForm", "Edit1Change")],
        };

        foreach (var step in steps)
        {
            var changes = step(tree);
            Assert.All(changes, c => Assert.False(c.IsEmpty));
            tree = SyntaxTree.Parse(SourceText.From(Path, Apply(tree, changes)));
        }

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(Expected("""
            unit MainForm;

            interface

            uses
              Forms;

            type
              TStartForm = class(TForm)
                OkButton: TButton;
                procedure OkButtonClick(Sender: TObject);
              private
              public
              end;

            implementation

            procedure TStartForm.OkButtonClick(Sender: TObject);
            begin

            end;

            end.
            """).Replace("begin\r\n\r\nend;", "begin\r\n  \r\nend;"), tree.Text.Text);
    }
}
