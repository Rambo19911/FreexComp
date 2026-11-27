using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Text;

/// <summary>Code templates (I-118, editor-features.md §6): built-ins, expansion, field mode, Tab expansion, <c>templates.json</c>.</summary>
[Trait("Phase", "3")]
public sealed class CodeTemplateTests
{
    private const string Head = "unit U;\ninterface\nimplementation\nprocedure P;\nbegin\n";

    /// <summary>IDE-ED-061 and IDE-ED-153, in this order (⏎ = <c>\n</c>, ⇥ = <c>\t</c>, one indent unit).</summary>
    private static readonly (string Abbreviation, string Body)[] SpecBuiltIns =
    [
        ("begin", "begin\n\t$end$\nend;"),
        ("if", "if $condition$ then\n\t$end$"),
        ("ife", "if $condition$ then\n\t$end$\nelse\n\t"),
        ("ifb", "if $condition$ then\nbegin\n\t$end$\nend;"),
        ("for", "for $i:I$ := $from:0$ to $to$ do\n\t$end$"),
        ("forb", "for $i:I$ := $from:0$ to $to$ do\nbegin\n\t$end$\nend;"),
        ("forin", "for $item$ in $collection$ do\n\t$end$"),
        ("while", "while $condition$ do\nbegin\n\t$end$\nend;"),
        ("repeat", "repeat\n\t$end$\nuntil $condition$;"),
        ("case", "case $expression$ of\n\t$value$: $end$;\nelse\nend;"),
        ("try", "try\n\t$end$\nfinally\n\t$cleanup$;\nend;"),
        ("trye", "try\n\t$end$\nexcept\n\ton E: $class:Exception$ do\n\t\t;\nend;"),
        ("proc", "procedure $name$($params$);\nbegin\n\t$end$\nend;"),
        ("func", "function $name$($params$): $type$;\nbegin\n\t$end$\nend;"),
        ("class", "$name:TMyClass$ = class($ancestor:TObject$)\nprivate\n\t$end$\npublic\nend;"),
        ("wl", "WriteLn($end$);"),

        // IDE-ED-153 (code-templates.md §4).
        ("else", "else\n\t$end$"),
        ("elseb", "else\nbegin\n\t$end$\nend;"),
        ("ifeb", "if $condition$ then\nbegin\n\t$end$\nend\nelse\nbegin\n\t\nend;"),
        ("ifa", "if Assigned($value$) then\n\t$end$"),
        ("ifn", "if $value$ = nil then\n\t$end$"),
        ("fordown", "for $i:I$ := $from$ downto $to:0$ do\n\t$end$"),
        ("forinb", "for $item$ in $collection$ do\nbegin\n\t$end$\nend;"),
        ("forv", "for var $i:I$ := $from:0$ to $to$ do\n\t$end$"),
        ("forinv", "for var $item$ in $collection$ do\n\t$end$"),
        ("with", "with $object$ do\nbegin\n\t$end$\nend;"),
        ("tryef", "try\n\ttry\n\t\t$end$\n\texcept\n\t\ton E: $class:Exception$ do\n\t\t\t;\n\tend;\nfinally\n\t$cleanup$;\nend;"),
        ("raise", "raise $class:Exception$.Create('$message$');"),
        ("rec", "$name:TMyRecord$ = record\n\t$end$\nend;"),
        ("intf", "$name:IMyInterface$ = interface\n\t$end$\nend;"),
        ("enum", "$name:TMyEnum$ = ($values$);"),
        ("arr", "array[$low:0$..$high$] of $type$$end$"),
        ("const", "const\n\t$name$ = $value$;"),
        ("var", "var\n\t$name$: $type$;"),
        ("type", "type\n\t$end$"),
        ("ctor", "constructor $class:=ClassName()$.Create;\nbegin\n\tinherited Create;\n\t$end$\nend;"),
        ("dtor", "destructor $class:=ClassName()$.Destroy;\nbegin\n\t$end$\n\tinherited;\nend;"),
        ("procm", "procedure $class:=ClassName()$.$name$($params$);\nbegin\n\t$end$\nend;"),
        ("funcm", "function $class:=ClassName()$.$name$($params$): $type$;\nbegin\n\t$end$\nend;"),
        ("prop", "property $name$: $type$ read F$name$ write F$name$;"),
        ("propr", "property $name$: $type$ read F$name$;"),
        ("unit", "unit $name:=UnitName()$;\n\ninterface\n\n$end$\n\nimplementation\n\nend."),
        ("prog", "program $name:=UnitName()$;\n\n{$$APPTYPE CONSOLE}\n\nbegin\n\t$end$\nend."),
        ("region", "{$$REGION '$name$'}\n$selection$$end$\n{$$ENDREGION}"),
        ("todo", "// TODO: $end$"),
        ("hdr", "// $unit:=UnitName()$ - $date:=Date()$$end$"),
        ("wlf", "WriteLn(Format('$format$', [$args$]));$end$"),
        ("fan", "FreeAndNil($object$);$end$"),
    ];

    private static async Task<(IdeServices Services, InMemoryFileSystem Fs, TextDocumentViewModel Doc)> OpenAsync(string text, Action<IdeServices>? before = null)
    {
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        services.Settings.Set(SettingsCatalog.EditorTabWidth, 2);
        services.Settings.Set(SettingsCatalog.EditorInsertSpaces, true);
        before?.Invoke(services);
        fs.CreateDirectory(@"C:\t");
        var caret = text.IndexOf('|', StringComparison.Ordinal);
        fs.WriteAllBytes(@"C:\t\U.pas", Encoding.UTF8.GetBytes(text.Remove(caret, 1)));
        var doc = Assert.IsType<TextDocumentViewModel>((await services.Documents.OpenAsync(@"C:\t\U.pas")).Value);
        services.Documents.Activate(doc);
        doc.Selection = TextSelection.At(caret);
        return (services, fs, doc);
    }

    /// <summary>The text with the selection marked: <c>[</c>…<c>]</c>, or <c>|</c> at an empty caret.</summary>
    private static string Marked(TextDocumentViewModel doc)
    {
        var s = doc.Selection;
        var text = doc.Buffer.Text.ReplaceLineEndings("\n");
        return s.IsEmpty ? text.Insert(s.Caret, "|") : text.Insert(s.End, "]").Insert(s.Start, "[");
    }

    private static void Type(TextDocumentViewModel doc, string text)
    {
        foreach (var c in text)
            Assert.True(doc.TypeText(c.ToString()));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-061")]
    [Trait("Req", "IDE-ED-153")]
    [Trait("Req", "IDE-SET-080")]
    public void BuiltInTemplatesMatchSpec()
    {
        var builtIns = CodeTemplateService.BuiltIns;
        Assert.Equal(48, SpecBuiltIns.Length);
        Assert.Equal(SpecBuiltIns.Select(t => t.Abbreviation), builtIns.Select(t => t.Abbreviation));
        foreach (var (abbreviation, body) in SpecBuiltIns)
        {
            var template = builtIns.Single(t => t.Abbreviation == abbreviation);
            Assert.Equal(body, template.Body);
            Assert.Equal("pascal", template.Language);
            Assert.False(string.IsNullOrWhiteSpace(template.Description), abbreviation);
            Assert.Equal(Strings.ResourceManager.GetString("Template_" + abbreviation, CultureInfo.CurrentCulture), template.Description); // IDE-ED-153
        }
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-153")]
    [Trait("Req", "IDE-ED-152")]
    public void EveryBuiltInParses()
    {
        // Every built-in passes the checks of a templates folder file (IDE-SET-083): abbreviation, body, functions, $ syntax.
        var builtIns = CodeTemplateService.BuiltIns;
        Assert.Equal(builtIns.Count, builtIns.Select(t => t.Abbreviation.ToUpperInvariant()).Distinct().Count());
        foreach (var template in builtIns)
        {
            Assert.True(CodeTemplateExpander.IsValidAbbreviation(template.Abbreviation), template.Abbreviation);
            Assert.NotEqual("", template.Body);
            Assert.Null(CodeTemplateExpander.FindSyntaxError(template.Body));
            var expansion = CodeTemplateExpander.Expand(template.Body, "", "  ", "\n", "Sel", name => "<" + name + ">");
            Assert.All(expansion.Fields, f => Assert.True(CodeTemplateExpander.IsIdentifier(f.Name), f.Name));
        }

        // The expanded skeletons are Pascal without syntax errors.
        static FreexComp.Compiler.Syntax.SyntaxTree Parse(string text) =>
            FreexComp.Compiler.Syntax.SyntaxTree.Parse(FreexComp.Compiler.SourceText.From(@"C:\t\Skeleton.pas", text));
        static string Expanded(string abbreviation) =>
            CodeTemplateExpander.Expand(CodeTemplateService.BuiltIn(abbreviation)!.Body, "", "  ", "\n", evaluate: _ => "Skeleton").Text;
        Assert.Equal("unit Skeleton;\n\ninterface\n\n\n\nimplementation\n\nend.", Expanded("unit"));
        Assert.Empty(Parse(Expanded("unit")).Diagnostics);
        Assert.Equal("program Skeleton;\n\n{$APPTYPE CONSOLE}\n\nbegin\n  \nend.", Expanded("prog"));
        Assert.Empty(Parse(Expanded("prog")).Diagnostics);
        Assert.Equal("{$REGION ''}\nSel\n{$ENDREGION}", CodeTemplateExpander.Expand(CodeTemplateService.BuiltIn("region")!.Body, "", "  ", "\n", "Sel").Text);
        Assert.Equal("constructor Skeleton.Create;\nbegin\n  inherited Create;\n  \nend;", Expanded("ctor"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-060")]
    [Trait("Req", "IDE-ED-065")]
    [Trait("Req", "IDE-ED-062")]
    [Trait("Req", "IDE-ED-066")]
    [Trait("Req", "IDE-SET-080")]
    public Task ExpansionFieldsAndMirrors() => TestUiThread.RunAsync(() => ExpansionFieldsAndMirrorsOnUiThread()); // I-209

    private async Task ExpansionFieldsAndMirrorsOnUiThread()
    {
        // IDE-ED-060: fields, defaults, mirrors, $end$, $$, indentation of later lines, a leading tab = one indent unit.
        var expansion = CodeTemplateExpander.Expand("a $x:1$ $$ $x$\n\tb$end$\nc", "  ", "  ", "\r\n");
        Assert.Equal("a 1 $ 1\r\n    b\r\n  c", expansion.Text);
        var field = Assert.Single(expansion.Fields);
        Assert.Equal("x", field.Name);
        Assert.Equal([(2, 1), (6, 1)], field.Occurrences);
        Assert.Equal(expansion.Text.IndexOf("b", StringComparison.Ordinal) + 1, expansion.EndOffset);
        Assert.Equal(7, CodeTemplateExpander.Expand("WriteLn", "", "  ", "\n").EndOffset); // no $end$: the end of the expansion

        // A user template from templates.json (a mirror), a modified built-in and a tombstone replace or hide built-ins.
        var (services, fs, doc) = await OpenAsync(Head + "  |\nend;\nend.\n", s =>
        {
            var roaming = s.UserPaths.RoamingDirectory;
            s.FileSystem.CreateDirectory(roaming);
            s.FileSystem.WriteAllBytes(Path.Combine(roaming, "templates.json"), Encoding.UTF8.GetBytes("""
                { "formatVersion": 1, "templates": [
                  { "abbreviation": "inc", "description": "increment", "language": "pascal", "body": "$v:Count$ := $v$ + 1;$end$" },
                  { "abbreviation": "wl", "description": "write line", "language": "pascal", "body": "WriteLn('$text$');" },
                  { "abbreviation": "trye", "description": "", "language": "pascal", "body": "", "deleted": true } ] }
                """));
        });
        var templates = doc.Templates!;
        Assert.Equal("WriteLn('$text$');", templates.Find("WL")!.Body); // case-insensitive
        Assert.Null(templates.Find("trye"));
        Assert.NotNull(templates.Find("try"));

        // Field mode: the first field selected, typing updates its mirror live.
        Type(doc, "inc");
        Assert.True(doc.HandleKey("Tab"));
        Assert.Equal(Head + "  [Count] := Count + 1;\nend;\nend.\n", Marked(doc));
        Assert.True(doc.InTemplateFieldMode);
        Type(doc, "Total");
        Assert.Equal(Head + "  Total| := Total + 1;\nend;\nend.\n", Marked(doc));
        Assert.True(doc.HandleKey("Backspace"));
        Assert.Equal(Head + "  Tota| := Tota + 1;\nend;\nend.\n", Marked(doc));

        // Tab on the last field jumps to $end$ and ends field mode.
        Assert.True(doc.HandleKey("Tab"));
        Assert.Equal(Head + "  Tota := Tota + 1;|\nend;\nend.\n", Marked(doc));
        Assert.False(doc.InTemplateFieldMode);

        // Undo in two steps: the field edits, then the expansion.
        await ((FreexComp.Ide.Core.Commands.ICommandTarget)doc).ExecuteAsync("edit.undo", null);
        Assert.Equal(Head + "  Count := Count + 1;\nend;\nend.\n", doc.Buffer.Text.ReplaceLineEndings("\n"));
        await ((FreexComp.Ide.Core.Commands.ICommandTarget)doc).ExecuteAsync("edit.undo", null);
        Assert.Equal(Head + "  inc\nend;\nend.\n", doc.Buffer.Text.ReplaceLineEndings("\n"));

        // Several fields: Tab / Shift+Tab move (selecting the content); Enter on the last field goes to $end$.
        doc.Selection = TextSelection.At(doc.Buffer.Text.IndexOf("inc", StringComparison.Ordinal));
        doc.Selection = new TextSelection(doc.Selection.Caret, doc.Selection.Caret + 3);
        Assert.True(doc.TypeText("for"));
        Assert.True(doc.HandleKey("Tab"));
        Assert.Equal(Head + "  for [I] := 0 to  do\n    \nend;\nend.\n", Marked(doc));
        Assert.True(doc.HandleKey("Tab"));
        Assert.Equal(Head + "  for I := [0] to  do\n    \nend;\nend.\n", Marked(doc));
        Assert.True(doc.HandleKey("Shift+Tab"));
        Assert.Equal(Head + "  for [I] := 0 to  do\n    \nend;\nend.\n", Marked(doc));
        Assert.True(doc.HandleKey("Tab"));
        Assert.True(doc.HandleKey("Tab"));
        Assert.Equal(Head + "  for I := 0 to | do\n    \nend;\nend.\n", Marked(doc)); // an empty field
        Type(doc, "9");
        Assert.True(doc.HandleKey("Enter"));
        Assert.Equal(Head + "  for I := 0 to 9 do\n    |\nend;\nend.\n", Marked(doc));
        Assert.False(doc.InTemplateFieldMode);

        // Escape, and a caret move out of all fields, end field mode leaving the text as it is.
        Type(doc, "wl");
        Assert.True(doc.HandleKey("Tab"));
        Assert.True(doc.InTemplateFieldMode);
        Assert.True(doc.HandleKey("Escape"));
        Assert.False(doc.InTemplateFieldMode);
        Assert.Contains("    WriteLn('');\n", doc.Buffer.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        doc.Selection = TextSelection.At(doc.Buffer.Text.IndexOf("WriteLn('');", StringComparison.Ordinal) + 12);
        Type(doc, "\n    inc");
        Assert.True(doc.HandleKey("Tab"));
        Assert.True(doc.InTemplateFieldMode);
        doc.Selection = TextSelection.At(0);
        Assert.False(doc.InTemplateFieldMode);

        // IDE-ED-062: Ctrl+J lists only templates, filtered by the identifier left of the caret; committing expands.
        var session = Assert.IsType<CompletionSession>(doc.Completion);
        var line = doc.Buffer.Text.IndexOf("end;\nend.", StringComparison.Ordinal);
        doc.Selection = TextSelection.At(line);
        Type(doc, "  wh\n");
        doc.Selection = TextSelection.At(line + 4);
        Assert.True(services.Commands.CanExecute("edit.insertTemplate"));
        await services.Commands.ExecuteAsync("edit.insertTemplate");
        Assert.True(session.IsOpen);
        Assert.All(session.Items, i => Assert.Equal(FreexComp.LanguageService.CompletionItemKind.Snippet, i.Kind));
        Assert.Equal(["while"], session.Items.Select(i => i.Label));
        Assert.Equal("while loop", session.Items[0].Detail);
        Assert.True(doc.HandleKey("Enter"));
        Assert.Contains("\n  while | do\n  begin\n    \n  end;\n", Marked(doc), StringComparison.Ordinal); // the empty field selected

        // IDE-ED-066: an unknown abbreviation.
        Assert.True(doc.HandleKey("Escape"));
        var statuses = new List<string?>();
        services.Status.Changed += (_, _) => statuses.Add(services.Status.Message);
        doc.Selection = TextSelection.At(0);
        Type(doc, "zork ");
        doc.Selection = TextSelection.At(4);
        await services.Commands.ExecuteAsync("edit.insertTemplate");
        Assert.False(session.IsOpen);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Editor_TemplateNotFound, "zork"), statuses[^1]);
        _ = fs;
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-064")]
    public Task TabExpansionRules() => TestUiThread.RunAsync(() => TabExpansionRulesOnUiThread()); // I-209

    private async Task TabExpansionRulesOnUiThread()
    {
        // Right after an abbreviation (case-insensitive): expands, replacing it, at the line's indentation.
        var (services, _, doc) = await OpenAsync(Head + "  BEGIN|\nend;\nend.\n");
        Assert.True(doc.HandleKey("Tab"));
        Assert.Equal(Head + "  begin\n    |\n  end;\nend;\nend.\n", Marked(doc));
        Assert.False(doc.InTemplateFieldMode); // no fields: the caret at $end$

        // Not after a longer identifier, nor with a selection, nor inside a comment or string: an ordinary Tab.
        foreach (var text in (string[])[Head + "  xbegin|\nend;\nend.\n", Head + "  // begin|\nend;\nend.\n", Head + "  s := 'begin|';\nend;\nend.\n",
                     Head + "  begi|\nend;\nend.\n"])
        {
            (_, _, doc) = await OpenAsync(text);
            var before = doc.Buffer.Text;
            doc.HandleKey("Tab");
            Assert.DoesNotContain("end;\nend;", doc.Buffer.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
            Assert.NotEqual(before, doc.Buffer.Text); // indented instead
        }

        (_, _, doc) = await OpenAsync(Head + "  begin|\nend;\nend.\n");
        doc.Selection = new TextSelection(doc.Selection.Caret - 5, doc.Selection.Caret);
        doc.HandleKey("Tab");
        Assert.DoesNotContain("end;\nend;", doc.Buffer.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // With editor.templates.expandOnTab off: an ordinary Tab.
        (services, _, doc) = await OpenAsync(Head + "  begin|\nend;\nend.\n", s => s.Settings.Set(SettingsCatalog.EditorTemplatesExpandOnTab, false));
        doc.HandleKey("Tab");
        Assert.StartsWith(Head + "  begin ", Marked(doc), StringComparison.Ordinal);
        Assert.DoesNotContain("end;\nend;", doc.Buffer.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        _ = services;
    }

    /// <summary>Opens <paramref name="text"/> (caret at <c>|</c>) as <paramref name="path"/> with a user template of <paramref name="body"/> named <c>t</c>.</summary>
    private static async Task<(IdeServices Services, TextDocumentViewModel Doc)> OpenWithTemplateAsync(string text, string body, string path = @"C:\t\U.pas")
    {
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        services.Settings.Set(SettingsCatalog.EditorTabWidth, 2);
        services.Settings.Set(SettingsCatalog.EditorInsertSpaces, true);
        services.Templates.Save([.. services.Templates.Templates, new CodeTemplate("t", "test", "pascal", body)]);
        fs.CreateDirectory(Path.GetDirectoryName(path)!);
        var caret = text.IndexOf('|', StringComparison.Ordinal);
        fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text.Remove(caret, 1)));
        var doc = Assert.IsType<TextDocumentViewModel>((await services.Documents.OpenAsync(path)).Value);
        services.Documents.Activate(doc);
        doc.Selection = TextSelection.At(caret);
        return (services, doc);
    }

    /// <summary>Types <c>t</c> and Tab at the caret: the expansion of the test template.</summary>
    private static string ExpandT(TextDocumentViewModel doc)
    {
        Type(doc, "t");
        Assert.True(doc.HandleKey("Tab"));
        return doc.Buffer.Text.ReplaceLineEndings("\n");
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-152")]
    public Task FunctionsUnitNameClassNameDateTime() => TestUiThread.RunAsync(() => FunctionsOnUiThread());

    private async Task FunctionsOnUiThread()
    {
        const string Body = "$u:=UnitName()$|$c:=classname()$|$d:=DATE()$ $t:=Time()$";

        // In a method implementation: the unit name from the syntax tree, the class of the method, the local date and time.
        var (services, doc) = await OpenWithTemplateAsync(
            "unit Foo.Bar;\ninterface\ntype\n  TFoo = class\n    procedure Go;\n  end;\nimplementation\nprocedure TFoo.Go;\nbegin\n  |\nend;\nend.\n", Body);
        var now = services.Clock.Now.LocalDateTime;
        var stamp = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + now.ToString("HH:mm", CultureInfo.InvariantCulture);
        Assert.Contains("\n  Foo.Bar|TFoo|" + stamp + "\n", ExpandT(doc), StringComparison.Ordinal);

        // The value is the field's initial text: the first field is selected and editable, its mirror follows.
        (_, doc) = await OpenWithTemplateAsync("program Hello;\nbegin\n  |\nend.\n", "$u:=UnitName()$ := $u$;");
        Assert.Contains("\n  Hello := Hello;\n", ExpandT(doc), StringComparison.Ordinal);
        Assert.True(doc.InTemplateFieldMode);
        Assert.Equal("Hello", doc.Buffer.GetText(doc.Selection.Start, doc.Selection.End - doc.Selection.Start));
        Type(doc, "X");
        Assert.Contains("\n  X := X;\n", doc.Buffer.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // Inside a type declaration (a record): its name; a nested routine of a method: the method's class.
        (_, doc) = await OpenWithTemplateAsync("unit U;\ninterface\ntype\n  TRec = record\n    |\n  end;\nimplementation\nend.\n", "[$c:=ClassName()$]");
        Assert.Contains("\n    [TRec]\n", ExpandT(doc), StringComparison.Ordinal);
        (_, doc) = await OpenWithTemplateAsync(
            "unit U;\ninterface\nimplementation\nprocedure TOuter.TInner.Run;\n  procedure Local;\n  begin\n    |\n  end;\nbegin\nend;\nend.\n", "[$c:=ClassName()$]");
        Assert.Contains("\n    [TOuter.TInner]\n", ExpandT(doc), StringComparison.Ordinal);

        // A plain routine outside any type: empty. A library: its name.
        (_, doc) = await OpenWithTemplateAsync("unit U;\ninterface\nimplementation\nprocedure P;\nbegin\n  |\nend;\nend.\n", "[$c:=ClassName()$]");
        Assert.Contains("\n  []\n", ExpandT(doc), StringComparison.Ordinal);
        (_, doc) = await OpenWithTemplateAsync("library Tools;\nbegin\n  |\nend.\n", "[$u:=UnitName()$]");
        Assert.Contains("\n  [Tools]\n", ExpandT(doc), StringComparison.Ordinal);

        // No unit header: the file name without extension; no file either: Unit1.
        (_, doc) = await OpenWithTemplateAsync("|\n", "[$u:=UnitName()$]", @"C:\t\Helpers.inc.pas");
        Assert.Equal("[Helpers.inc]\n", ExpandT(doc));
        Assert.Equal("Unit1", TemplateFunctions.UnitName(null, null));
        Assert.Equal("Unit1", TemplateFunctions.UnitName(null, ""));

        // The expander itself: functions are case-insensitive, computed once per field.
        var calls = new List<string>();
        var expansion = CodeTemplateExpander.Expand("$a:=date()$ $a$ $b:=TIME()$", "", "  ", "\n", evaluate: name =>
        {
            calls.Add(name);
            return name == "Date" ? "D" : "T";
        });
        Assert.Equal("D D T", expansion.Text);
        Assert.Equal(["Date", "Time"], calls);
        Assert.Equal(["a", "b"], expansion.Fields.Select(f => f.Name));
        Assert.Equal(["ClassName", "Date", "Time", "UnitName"], TemplateFunctions.Names.Order(StringComparer.Ordinal));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-152")]
    [Trait("Req", "IDE-SET-083")]
    public void UnknownFunctionIsSyntaxError()
    {
        Assert.Equal(new TemplateSyntaxError(TemplateSyntaxErrorKind.UnknownFunction, 2, 5, "Nope"), CodeTemplateExpander.FindSyntaxError("x\n  y $a:=Nope()$"));
        Assert.Equal("UnitName", CodeTemplateExpander.FindSyntaxError("$a:=UnitName$")?.Name); // no parentheses
        Assert.Equal("Date(1)", CodeTemplateExpander.FindSyntaxError("$a:=Date(1)$")?.Name); // no arguments
        Assert.Equal("", CodeTemplateExpander.FindSyntaxError("$a:=$")?.Name);
        Assert.Null(CodeTemplateExpander.FindSyntaxError("$a:=unitname()$ $b:=ClassName()$ $c:=Date()$ $d:=time()$ $e:x=1$"));

        // A folder file with an unknown function is skipped and reported (IDE-SET-083).
        var fs = new InMemoryFileSystem();
        var log = new RecordingLog();
        var userDir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"), "user");
        fs.CreateDirectory(Path.Combine(userDir, "roaming", "templates"));
        fs.WriteAllBytes(Path.Combine(userDir, "roaming", "templates", "f.json"), Encoding.UTF8.GetBytes(
            """{ "formatVersion": 1, "templates": [ { "abbreviation": "ok", "language": "pascal", "body": "ok;" }, { "abbreviation": "f", "language": "pascal", "body": "$x:=Nope()$" } ] }"""));
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.Log = log;
            o.UserDir = userDir;
        });
        Assert.Null(services.Templates.Find("ok"));
        var row = Assert.Single(services.Messages.Items);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Templates_FileSkipped, "f.json",
            string.Format(CultureInfo.CurrentCulture, Strings.Msg_Templates_UnknownFunction, "f", "Nope")), row.Text);

        // A template that was not checked (templates.json) keeps an unknown call as the literal default text.
        Assert.Equal("=Nope()", CodeTemplateExpander.Expand("$x:=Nope()$", "", "  ", "\n", evaluate: _ => "never").Text);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-152")]
    [Trait("Req", "IDE-ED-060")]
    public void DollarDollarStaysLiteral()
    {
        Assert.Equal("{$APPTYPE CONSOLE}", CodeTemplateExpander.Expand("{$$APPTYPE CONSOLE}", "", "  ", "\n").Text);
        var expansion = CodeTemplateExpander.Expand("$$$x:a$$$ $$y$$", "", "  ", "\n");
        Assert.Equal("$a$ $y$", expansion.Text);
        Assert.Equal(["x"], expansion.Fields.Select(f => f.Name));
        Assert.Equal("{$IFDEF D}U{$ENDIF}", CodeTemplateExpander.Expand("{$$IFDEF D}$u:=UnitName()${$$ENDIF}", "", "  ", "\n", evaluate: _ => "U").Text);
        Assert.Null(CodeTemplateExpander.FindSyntaxError("{$$APPTYPE CONSOLE}"));
        Assert.Null(CodeTemplateExpander.FindSyntaxError("$$$x:a$$$ $$y$$"));
        Assert.Equal(new TemplateSyntaxError(TemplateSyntaxErrorKind.Unterminated, 1, 2), CodeTemplateExpander.FindSyntaxError("{$APPTYPE CONSOLE}"));
        Assert.Equal(new TemplateSyntaxError(TemplateSyntaxErrorKind.Unterminated, 1, 3), CodeTemplateExpander.FindSyntaxError("$$$"));
    }
}
