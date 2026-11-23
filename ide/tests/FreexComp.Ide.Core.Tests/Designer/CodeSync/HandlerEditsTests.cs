using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Designer.CodeSync;

/// <summary>code-sync.md §5 DSG-CS-050, §6, §7: event handler matching, creation, renaming, navigation and removal (D-065 … D-068).</summary>
[Trait("Phase", "5")]
public sealed class HandlerEditsTests
{
    private static readonly Lazy<ComponentCatalog> LoadedCatalog = new(() =>
    {
        var result = CatalogLoader.Load([Path.Combine(AppContext.BaseDirectory, "TestData", "metadata", "index.json")]);
        Assert.Empty(result.Problems);
        return result.Catalog;
    });

    private static ComponentCatalog Catalog => LoadedCatalog.Value;

    private static FormUnit Unit(string file, string formType) => new(TestUnits.Buffer(file), formType);

    private static FormUnit UnitFromText(string text, string formType) => new(new StringTextBuffer(text), formType);

    private static FormDocument Form(string text)
    {
        var result = FormParser.Parse(text.Replace("\r\n", "\n", StringComparison.Ordinal), "test.form", Catalog);
        return result.Document ?? throw new InvalidOperationException("form did not parse");
    }

    private const string Notify = "procedure(Sender: TObject)";

    /// <summary>A unit whose form class <c>TF</c> declares <paramref name="members"/> in its managed section.</summary>
    private static string ClassWith(string members) =>
        "unit U;\r\ninterface\r\ntype\r\n  TF = class(TForm)\r\n" + members + "  end;\r\nimplementation\r\nend.\r\n";

    [Theory, Trait("Req", "DSG-CS-050")]
    [InlineData("procedure A(Sender: TObject);", Notify, true)]
    [InlineData("procedure A(X: TObject);", Notify, true)]                                        // parameter names are ignored
    [InlineData("procedure A( sender :tobject );", Notify, true)]                                 // case and whitespace
    [InlineData("procedure A(Sender: System.TObject);", Notify, true)]                            // unit qualifier stripped
    [InlineData("procedure A(Sender: TObject);", "procedure(Sender: Classes.TObject)", true)]
    [InlineData("procedure A;", Notify, false)]                                                   // parameter count
    [InlineData("procedure A(Sender, Other: TObject);", Notify, false)]
    [InlineData("procedure A(Sender: TObject; Other: TObject);", "procedure(Sender, Other: TObject)", true)]
    [InlineData("procedure A(Sender: TComponent);", Notify, false)]                               // type
    [InlineData("procedure A(const Sender: TObject);", Notify, false)]                            // modifier
    [InlineData("procedure A(Sender: TObject; var Key: Char);", "procedure(Sender: TObject; var Key: Char)", true)]
    [InlineData("procedure A(Sender: TObject; Key: Char);", "procedure(Sender: TObject; var Key: Char)", false)]
    [InlineData("procedure A(Sender: TObject; out Key: Char);", "procedure(Sender: TObject; var Key: Char)", false)]
    [InlineData("function A(Sender: TObject): Boolean;", "function(Sender: TObject): Boolean", true)]
    [InlineData("function A(Sender: TObject): Integer;", "function(Sender: TObject): Boolean", false)]  // result type
    [InlineData("function A(Sender: TObject): Boolean;", Notify, false)]                          // kind
    [InlineData("procedure A(Sender: TObject);", "function(Sender: TObject): Boolean", false)]
    [InlineData("class procedure A(Sender: TObject);", Notify, false)]                            // class methods never match
    [InlineData("constructor A(Sender: TObject);", Notify, false)]
    [InlineData("destructor A(Sender: TObject);", Notify, false)]
    [InlineData("procedure A(Sender: TObject); overload;", Notify, false)]                       // overloads never match
    [InlineData("procedure A;", "procedure", true)]
    [InlineData("procedure A();", "procedure", true)]
    public void SignatureMatching(string declaration, string signature, bool matches)
    {
        var method = Assert.IsType<MethodDeclarationSyntax>(SyntaxFactory.ParseNode<MemberDeclarationSyntax>(declaration));
        Assert.Equal(matches, HandlerEdits.SignatureMatches(method, signature));
    }

    [Fact, Trait("Req", "DSG-CS-050"), Trait("Req", "DSG-CS-031")]
    public void CompatibleMethodsAndMemberLookup()
    {
        // GetCompatibleMethods takes the event signature type and resolves it through the catalog; sorted by name.
        var service = new CodeSyncService(Unit("WithHandlers.pas", "THandlersForm"), Catalog);
        Assert.Equal(["FormCreate", "OkButtonClick"], service.GetCompatibleMethods("TNotifyEvent"));
        Assert.Equal(["OkButtonKeyPress"], service.GetCompatibleMethods("TKeyPressEvent"));
        Assert.Empty(service.GetCompatibleMethods("TNoSuchEvent"));

        // Methods of every section count.
        var published = new CodeSyncService(Unit("ExplicitPublished.pas", "TPublishedForm"), Catalog);
        Assert.Equal(["CancelButtonClick", "OkButtonClick"], published.GetCompatibleMethods("TNotifyEvent"));

        Assert.Equal(MemberLookup.Field, service.LookupMember("okbutton"));
        Assert.Equal(MemberLookup.Method, service.LookupMember("FormCreate"));
        Assert.Equal(MemberLookup.None, service.LookupMember("Button1"));
        Assert.Equal(MemberLookup.Field, published.LookupMember("FCount"));        // any section
        Assert.Equal(MemberLookup.Method, published.LookupMember("Count"));

        // Published members of the ancestors as far as the catalog knows them, and the fixed list.
        Assert.Equal(MemberLookup.Other, service.LookupMember("Caption"));
        Assert.Equal(MemberLookup.Other, service.LookupMember("OnCreate"));
        Assert.Equal(MemberLookup.Other, service.LookupMember("Components"));
        Assert.Equal(MemberLookup.Other, service.LookupMember("show"));

        // A property of the class itself.
        var withProperty = new CodeSyncService(UnitFromText(ClassWith("    property Total: Integer read FTotal;\r\n"), "TF"), Catalog);
        Assert.Equal(MemberLookup.Other, withProperty.LookupMember("Total"));
    }

    [Fact, Trait("Req", "DSG-CS-060"), Trait("Req", "DSG-CS-061"), Trait("Req", "DSG-CS-062"), Trait("Req", "DSG-CS-071")]
    public void CreatesDeclarationAndStub()
    {
        var unit = Unit("MainForm.pas", "TMainForm");
        var service = new CodeSyncService(unit, Catalog);
        var edits = service.CreateHandler("NameEditChange", Notify);

        TestUnits.AssertGolden("CreatesDeclarationAndStub", unit.Buffer.Text);
        Assert.Equal(2, edits.Count);
        Assert.Equal("\r\n    procedure NameEditChange(Sender: TObject);", edits[0].InsertedText);
        Assert.Equal("\r\nprocedure TMainForm.NameEditChange(Sender: TObject);\r\nbegin\r\n  \r\nend;\r\n\r\n", edits[1].InsertedText);
        Assert.Contains("NameEditChange", service.HandlerCandidates);

        // One code-editor undo group.
        unit.Buffer.Undo();
        Assert.Equal(TestUnits.Read("MainForm.pas"), unit.Buffer.Text);
        Assert.False(unit.Buffer.CanUndo);
    }

    [Fact, Trait("Req", "DSG-CS-060"), Trait("Req", "DSG-CS-061"), Trait("Req", "DSG-CS-062"), Trait("Req", "DSG-CS-064")]
    public void StubPositionVariants()
    {
        const string Stub = "procedure TF.Click1(Sender: TObject);\r\nbegin\r\n  \r\nend;\r\n";

        // After the last method implementation of the form class, not after another routine.
        var afterMethod = UnitFromText(
            "unit U;\r\ninterface\r\ntype\r\n  TF = class(TForm)\r\n    procedure FormCreate(Sender: TObject);\r\n  end;\r\nimplementation\r\n\r\n"
            + "procedure TF.FormCreate(Sender: TObject);\r\nbegin\r\nend;\r\n\r\nprocedure Helper;\r\nbegin\r\nend;\r\n\r\nend.\r\n", "TF");
        HandlerEdits.CreateHandler(afterMethod, "Click1", Notify);
        Assert.Equal(
            "unit U;\r\ninterface\r\ntype\r\n  TF = class(TForm)\r\n    procedure FormCreate(Sender: TObject);\r\n    procedure Click1(Sender: TObject);\r\n  end;\r\n"
            + "implementation\r\n\r\nprocedure TF.FormCreate(Sender: TObject);\r\nbegin\r\nend;\r\n\r\n" + Stub
            + "\r\nprocedure Helper;\r\nbegin\r\nend;\r\n\r\nend.\r\n", afterMethod.Buffer.Text);

        // No method yet: after the {$FORM} directive line; the declaration after the class heading (heading indent + IU).
        var afterDirective = Unit("EmptyClass.pas", "TEmptyForm");
        HandlerEdits.CreateHandler(afterDirective, "FormCreate", Notify);
        Assert.Equal(TestUnits.Read("EmptyClass.pas")
            .Replace("  TEmptyForm = class(TForm)\r\n", "  TEmptyForm = class(TForm)\r\n    procedure FormCreate(Sender: TObject);\r\n", StringComparison.Ordinal)
            .Replace("{$FORM EmptyClass.form}\r\n\r\n",
                "{$FORM EmptyClass.form}\r\n\r\nprocedure TEmptyForm.FormCreate(Sender: TObject);\r\nbegin\r\n  \r\nend;\r\n\r\n", StringComparison.Ordinal),
            afterDirective.Buffer.Text);

        // Before initialization (one blank line before it).
        var beforeInitialization = Unit("WithInitialization.pas", "TInitForm");
        HandlerEdits.CreateHandler(beforeInitialization, "FormCreate", Notify);
        Assert.Contains("{$FORM WithInitialization.form}\r\n\r\nprocedure TInitForm.FormCreate(Sender: TObject);\r\nbegin\r\n  \r\nend;\r\n\r\ninitialization\r\n",
            beforeInitialization.Buffer.Text, StringComparison.Ordinal);

        // No directive: after the implementation uses clause; else after the implementation line.
        var afterUses = UnitFromText(ClassWith("").Replace("implementation\r\n", "implementation\r\n\r\nuses\r\n  Dialogs;\r\n", StringComparison.Ordinal), "TF");
        HandlerEdits.CreateHandler(afterUses, "Click1", Notify);
        Assert.EndsWith("implementation\r\n\r\nuses\r\n  Dialogs;\r\n\r\n" + Stub + "\r\nend.\r\n", afterUses.Buffer.Text, StringComparison.Ordinal);
        var afterKeyword = UnitFromText(ClassWith(""), "TF");
        HandlerEdits.CreateHandler(afterKeyword, "Click1", Notify);
        Assert.EndsWith("implementation\r\n\r\n" + Stub + "\r\nend.\r\n", afterKeyword.Buffer.Text, StringComparison.Ordinal);

        // A function; the parameter list normalised to single spaces after ':' and ';'.
        var function = UnitFromText(ClassWith(""), "TF");
        HandlerEdits.CreateHandler(function, "CanClose", "function(Sender:TObject;var Key:  Char):Boolean");
        Assert.Contains("    function CanClose(Sender: TObject; var Key: Char): Boolean;\r\n", function.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("function TF.CanClose(Sender: TObject; var Key: Char): Boolean;\r\nbegin\r\n  \r\nend;\r\n", function.Buffer.Text, StringComparison.Ordinal);

        // A parameterless signature: no parentheses.
        var parameterless = UnitFromText(ClassWith(""), "TF");
        HandlerEdits.CreateHandler(parameterless, "Tick", "procedure");
        Assert.Contains("    procedure Tick;\r\n", parameterless.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("procedure TF.Tick;\r\nbegin\r\n", parameterless.Buffer.Text, StringComparison.Ordinal);

        // DSG-CS-064: declaration without implementation → only the implementation; and the other way round.
        var declared = UnitFromText(ClassWith("    procedure Click1(Sender: TObject);\r\n"), "TF");
        var onlyImplementation = HandlerEdits.CreateHandler(declared, "Click1", Notify);
        Assert.Single(onlyImplementation);
        Assert.Equal(ClassWith("    procedure Click1(Sender: TObject);\r\n").Replace("end.\r\n", Stub + "\r\nend.\r\n", StringComparison.Ordinal)
            .Replace("implementation\r\n", "implementation\r\n\r\n", StringComparison.Ordinal), declared.Buffer.Text);
        var implemented = UnitFromText(ClassWith("").Replace("implementation\r\n", "implementation\r\n\r\n" + Stub + "\r\n", StringComparison.Ordinal), "TF");
        var onlyDeclaration = HandlerEdits.CreateHandler(implemented, "Click1", Notify);
        Assert.Equal("\r\n    procedure Click1(Sender: TObject);", Assert.Single(onlyDeclaration).InsertedText);
    }

    [Fact, Trait("Req", "DSG-CS-066")]
    public void RenameHandler()
    {
        var original = TestUnits.Read("WithHandlers.pas").Replace("begin\r\n\r\nend;", "begin\r\n  OkButtonClick(Self);\r\nend;", StringComparison.Ordinal);
        var unit = UnitFromText(original, "THandlersForm");
        var service = new CodeSyncService(unit, Catalog);
        service.NoteHandlerCandidate("OkButtonClick");

        var edits = service.RenameHandler("okbuttonclick", "AcceptClick");

        Assert.Equal(2, edits.Count);
        Assert.Equal(original
            .Replace("    procedure OkButtonClick(", "    procedure AcceptClick(", StringComparison.Ordinal)
            .Replace("THandlersForm.OkButtonClick(", "THandlersForm.AcceptClick(", StringComparison.Ordinal), unit.Buffer.Text);
        Assert.Contains("  OkButtonClick(Self);", unit.Buffer.Text, StringComparison.Ordinal); // calls are not changed
        Assert.Contains("AcceptClick", service.HandlerCandidates);
        Assert.DoesNotContain("OkButtonClick", service.HandlerCandidates);

        unit.Buffer.Undo();
        Assert.Equal(original, unit.Buffer.Text);
        Assert.False(unit.Buffer.CanUndo);
    }

    [Fact, Trait("Req", "DSG-CS-063"), Trait("Req", "DSG-CS-104")]
    public void NavigationCaretPosition()
    {
        int? caret = null;
        int Navigate(FormUnit unit, string method)
        {
            caret = null;
            new CodeSyncService(unit, Catalog, offset => caret = offset).NavigateToHandler(method);
            return caret ?? throw new InvalidOperationException("no navigation");
        }

        // (a) first statement / first comment after begin.
        var handlers = Unit("WithHandlers.pas", "THandlersForm");
        var text = handlers.Buffer.Text;
        Assert.Equal(text.IndexOf("Close;", StringComparison.Ordinal), Navigate(handlers, "OkButtonClick"));
        Assert.Equal(text.IndexOf("// keep", StringComparison.Ordinal), Navigate(handlers, "OkButtonKeyPress"));

        // (b) the end of the first whitespace-only line between begin and end.
        var formCreate = text.IndexOf("THandlersForm.FormCreate", StringComparison.Ordinal);
        Assert.Equal(text.IndexOf("begin\r\n", formCreate, StringComparison.Ordinal) + "begin\r\n".Length, Navigate(handlers, "FormCreate"));
        var spaces = UnitFromText(ClassWith("    procedure Click1(Sender: TObject);\r\n")
            .Replace("implementation\r\n", "implementation\r\nprocedure TF.Click1(Sender: TObject);\r\nbegin\r\n    \r\n\r\nend;\r\n", StringComparison.Ordinal), "TF");
        Assert.Equal(spaces.Buffer.Text.IndexOf("    \r\n\r\nend;", StringComparison.Ordinal) + 4, Navigate(spaces, "Click1"));

        // (c) begin and end on adjacent lines or the same line: the start of end.
        var published = Unit("ExplicitPublished.pas", "TPublishedForm");
        var ok = published.Buffer.Text.IndexOf("TPublishedForm.OkButtonClick", StringComparison.Ordinal);
        Assert.Equal(published.Buffer.Text.IndexOf("end;", ok, StringComparison.Ordinal), Navigate(published, "OkButtonClick"));
        var sameLine = UnitFromText(ClassWith("    procedure Click1(Sender: TObject);\r\n")
            .Replace("implementation\r\n", "implementation\r\nprocedure TF.Click1(Sender: TObject);\r\nbegin end;\r\n", StringComparison.Ordinal), "TF");
        Assert.Equal(sameLine.Buffer.Text.IndexOf("end;\r\nend.", StringComparison.Ordinal), Navigate(sameLine, "Click1"));

        // Navigation never modifies the text in cases (a) … (c).
        Assert.Equal(TestUnits.Read("WithHandlers.pas"), handlers.Buffer.Text);
        Assert.False(handlers.Buffer.CanUndo);
        Assert.False(published.Buffer.CanUndo);

        // A stub created by DSG-CS-062: line "begin" + 1, column 3.
        var main = Unit("MainForm.pas", "TMainForm");
        new CodeSyncService(main, Catalog).CreateHandler("NameEditChange", Notify);
        var stub = main.Buffer.Text.IndexOf("TMainForm.NameEditChange", StringComparison.Ordinal);
        Assert.Equal(main.Buffer.Text.IndexOf("begin\r\n", stub, StringComparison.Ordinal) + "begin\r\n".Length + 2, Navigate(main, "NameEditChange"));

        // Only the declaration exists: the implementation stub is created first, an undo group of its own.
        var declared = UnitFromText(ClassWith("    procedure Click1(Sender: TObject);\r\n"), "TF");
        var at = Navigate(declared, "Click1");
        Assert.Contains("procedure TF.Click1(Sender: TObject);\r\nbegin\r\n  \r\nend;\r\n", declared.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(declared.Buffer.Text.IndexOf("begin\r\n  \r\n", StringComparison.Ordinal) + "begin\r\n".Length + 2, at);
        declared.Buffer.Undo();
        Assert.Equal(ClassWith("    procedure Click1(Sender: TObject);\r\n"), declared.Buffer.Text);
        Assert.False(declared.Buffer.CanUndo);

        // No such method: nothing happens.
        caret = null;
        new CodeSyncService(Unit("MainForm.pas", "TMainForm"), Catalog, offset => caret = offset).NavigateToHandler("NoSuchMethod");
        Assert.Null(caret);
    }

    [Fact, Trait("Req", "DSG-CS-070")]
    public void EmptyDetection()
    {
        var service = new CodeSyncService(Unit("WithHandlers.pas", "THandlersForm"), Catalog);
        Assert.True(service.IsHandlerEmpty("FormCreate"));            // only whitespace
        Assert.False(service.IsHandlerEmpty("OkButtonClick"));        // a statement
        Assert.False(service.IsHandlerEmpty("OkButtonKeyPress"));     // a comment
        Assert.False(service.IsHandlerEmpty("NoSuchMethod"));

        string Implemented(string body) => ClassWith("    procedure Click1(Sender: TObject);\r\n")
            .Replace("implementation\r\n", "implementation\r\nprocedure TF.Click1(Sender: TObject);\r\n" + body + "\r\n", StringComparison.Ordinal);
        bool Empty(string body) => new CodeSyncService(UnitFromText(Implemented(body), "TF"), Catalog).IsHandlerEmpty("Click1");

        Assert.True(Empty("begin end;"));
        Assert.True(Empty("begin\r\n   \r\n\t\r\nend;"));
        Assert.False(Empty("begin\r\n  { note }\r\nend;"));
        Assert.False(Empty("begin\r\n  (* note *)\r\nend;"));
        Assert.False(Empty("var\r\n  I: Integer;\r\nbegin\r\nend;"));    // local declarations
        Assert.False(Empty("const\r\n  C = 1;\r\nbegin\r\nend;"));
        Assert.False(Empty("begin\r\n  Close;\r\nend;"));

        // A declaration without implementation is empty too.
        Assert.True(new CodeSyncService(UnitFromText(ClassWith("    procedure Click1(Sender: TObject);\r\n"), "TF"), Catalog).IsHandlerEmpty("Click1"));
    }

    [Fact, Trait("Req", "DSG-CS-071"), Trait("Req", "DSG-CS-072"), Trait("Req", "DSG-CS-073"), Trait("Req", "DSG-CS-076")]
    public void RemovesOnlyEmptyUnreferencedCandidates()
    {
        // The form no longer references OkButtonKeyPress (non-empty: a comment); FormCreate is referenced and empty.
        var formText = TestUnits.Read("WithHandlers.form");
        var form = Form(formText.Replace("    OnKeyPress = OkButtonKeyPress\n", "", StringComparison.Ordinal)
            .Replace("    OnKeyPress = OkButtonKeyPress\r\n", "", StringComparison.Ordinal));
        Assert.Null(form.FindByName("OkButton")!.FindProperty("OnKeyPress"));

        // "Unused": empty and unreferenced, but never touched by the designer in this session.
        var unit = Unit("WithHandlers.pas", "THandlersForm");
        HandlerEdits.CreateHandler(unit, "Unused", Notify);
        var expected = unit.Buffer.Text;

        var service = new CodeSyncService(unit, Catalog);
        service.CreateHandler("ExtraClick", Notify);                  // a candidate: created by the designer
        service.NoteHandlerCandidate("FormCreate");                     // referenced → kept
        service.NoteHandlerCandidate("OkButtonKeyPress");               // not empty → kept
        service.NoteHandlerCandidate("NotDeclared");                    // not declared → nothing

        var edits = service.RemoveEmptyUnreferencedHandlers(form);

        Assert.Equal(2, edits.Count);                                   // declaration line + implementation
        Assert.Equal(expected, unit.Buffer.Text);
        Assert.DoesNotContain("ExtraClick", service.HandlerCandidates);
        Assert.Contains("FormCreate", service.HandlerCandidates);

        // One code-editor undo group.
        unit.Buffer.Undo();
        Assert.Contains("ExtraClick", unit.Buffer.Text, StringComparison.Ordinal);
        Assert.Empty(service.RemoveEmptyUnreferencedHandlers(form));    // it left the set
        service.NoteHandlerCandidate("ExtraClick");                     // e.g. its event was cleared again

        // A referenced empty handler is never removed, even when it is a candidate.
        var referenced = Form(formText.Replace("    OnClick = OkButtonClick", "    OnClick = ExtraClick", StringComparison.Ordinal));
        Assert.Empty(service.RemoveEmptyUnreferencedHandlers(referenced));

        // Setting designer.removeEmptyHandlersOnSave = false turns the removal off.
        service.RemoveEmptyHandlersOnSave = false;
        Assert.Empty(service.RemoveEmptyUnreferencedHandlers(form));
        Assert.Contains("ExtraClick", unit.Buffer.Text, StringComparison.Ordinal);
        service.RemoveEmptyHandlersOnSave = true;
        Assert.Equal(2, service.RemoveEmptyUnreferencedHandlers(form).Count);
        Assert.Equal(expected, unit.Buffer.Text);
    }
}
