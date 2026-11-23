using FreexComp.Ide.Core.Designer.CodeSync;

namespace FreexComp.Ide.Core.Tests.Designer.CodeSync;

/// <summary>code-sync.md §2, §8: locating the form class and the code-sync states (D-061).</summary>
[Trait("Phase", "5")]
public sealed class FormClassLocatorTests
{
    [Fact, Trait("Req", "DSG-CS-012"), Trait("Req", "DSG-CS-013"), Trait("Req", "DSG-CS-014")]
    public void FindsClassAndManagedSection()
    {
        var standard = FormClassLocator.Locate(TestUnits.Read("MainForm.pas"), "TMainForm");
        Assert.Equal(CodeSyncState.Ok, standard.State);
        var main = standard.FormClass!;
        Assert.Equal("TMainForm", main.ClassName);
        Assert.Equal("TForm", main.AncestorName);
        Assert.Equal([("NameEdit", "TEdit"), ("GreetButton", "TButton")], main.Fields.Select(f => (f.Name, f.TypeText)));
        var click = Assert.Single(main.Methods);
        Assert.Equal(("GreetButtonClick", "procedure(Sender: TObject)", ""), (click.Name, click.Signature, click.Section));
        Assert.True(main.Implementations.ContainsKey("greetbuttonclick"));
        Assert.Equal(["Classes", "Controls", "Forms", "StdCtrls"], main.UsesClause!.Units.Select(u => u.Name.ToString()));
        Assert.Equal("MainForm.form", main.FormDirective!.Argument);
        Assert.Equal("MainForm", main.FormVariable!.Identifiers[0].Identifier.Text);

        // Unit qualifier of the root type name stripped; class name compared case-insensitively.
        Assert.Equal(CodeSyncState.Ok, FormClassLocator.Locate(TestUnits.Read("MainForm.pas"), "MainForm.tmainform").State);

        // The managed section ends at the first visibility specifier; methods come from every section; lookups of
        // implementations by heading TClass.Name.
        var published = FormClassLocator.Locate(TestUnits.Read("ExplicitPublished.pas"), "TPublishedForm").FormClass!;
        Assert.Equal(["OkButton"], published.Fields.Select(f => f.Name));
        Assert.Equal([("OkButtonClick", ""), ("Helper", "private"), ("CancelButtonClick", "published"), ("Count", "public")],
            published.Methods.Select(m => (m.Name, m.Section)));
        Assert.Equal("function: Integer", published.Methods[3].Signature);
        Assert.Equal(["OkButtonClick", "Helper", "CancelButtonClick", "Count"], published.Implementations.Keys);

        // Multi-name declarations give one field per name.
        var multi = FormClassLocator.Locate(TestUnits.Read("MultiNameFields.pas"), "TMultiForm").FormClass!;
        Assert.Equal([("Button1", "TButton"), ("Button2", "TButton"), ("Button3", "TButton"), ("Edit1", "TEdit")],
            multi.Fields.Select(f => (f.Name, f.TypeText)));
        Assert.Same(multi.Fields[0].Declaration, multi.Fields[2].Declaration);

        // An empty managed section; no interface uses clause; no directive.
        var noUses = FormClassLocator.Locate(TestUnits.Read("NoUses.pas"), "TNoUsesForm").FormClass!;
        Assert.Empty(noUses.Fields);
        Assert.Empty(noUses.Methods);
        Assert.Null(noUses.UsesClause);
        var implUses = FormClassLocator.Locate(TestUnits.Read("ImplUsesOnly.pas"), "TImplUsesForm").FormClass!;
        Assert.Null(implUses.UsesClause); // the implementation clause is not the interface clause
        var noDirective = FormClassLocator.Locate(TestUnits.Read("MainForm.pas").Replace("{$FORM MainForm.form}", "", StringComparison.Ordinal), "TMainForm");
        Assert.Null(noDirective.FormClass!.FormDirective);
    }

    [Fact, Trait("Req", "DSG-CS-011"), Trait("Req", "DSG-CS-012")]
    public void StatesForErrorsAndMissingClass()
    {
        // A parser error → UnitHasErrors, with the first parser error for "Go to First Error".
        var broken = FormClassLocator.Locate(TestUnits.Read("SyntaxError.pas"), "TBrokenForm");
        Assert.Equal(CodeSyncState.UnitHasErrors, broken.State);
        Assert.Null(broken.FormClass);
        Assert.NotNull(broken.FirstError);
        Assert.StartsWith("E1", broken.FirstError.Code, StringComparison.Ordinal);

        // Semantic errors (an undeclared identifier in a body) do not count.
        var semantic = TestUnits.Read("MainForm.pas").Replace("begin\r\n\r\nend;", "begin\r\n  UndeclaredThing := 1;\r\nend;", StringComparison.Ordinal);
        Assert.Contains("UndeclaredThing", semantic, StringComparison.Ordinal);
        Assert.Equal(CodeSyncState.Ok, FormClassLocator.Locate(semantic, "TMainForm").State);

        // Not found: another class name, a forward declaration in the interface (the full class only in the
        // implementation), a record, a program.
        var noClass = TestUnits.Read("NoFormClass.pas");
        Assert.Equal(CodeSyncState.FormClassNotFound, FormClassLocator.Locate(noClass, "TMissingForm").State);
        Assert.Equal(CodeSyncState.FormClassNotFound, FormClassLocator.Locate(noClass, "TMissingRecord").State);
        Assert.Equal(CodeSyncState.FormClassNotFound, FormClassLocator.Locate(noClass, "TNoSuchForm").State);
        Assert.Equal(CodeSyncState.Ok, FormClassLocator.Locate(noClass, "TOtherForm").State);
        Assert.Equal(CodeSyncState.FormClassNotFound, FormClassLocator.Locate(TestUnits.Read("MainForm.pas"), "TOtherForm").State);
        Assert.Equal(CodeSyncState.FormClassNotFound,
            FormClassLocator.Locate("program P;\r\ntype\r\n  TMainForm = class(TForm)\r\n  end;\r\nbegin\r\nend.\r\n", "TMainForm").State);
    }
}
