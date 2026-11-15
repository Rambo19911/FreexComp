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

    /// <summary>IDE-ED-061 (⏎ = <c>\n</c>, ⇥ = <c>\t</c>, one indent unit).</summary>
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
    [Trait("Req", "IDE-SET-080")]
    public void BuiltInTemplatesMatchSpec()
    {
        var builtIns = CodeTemplateService.BuiltIns;
        Assert.Equal(SpecBuiltIns.Select(t => t.Abbreviation), builtIns.Select(t => t.Abbreviation));
        foreach (var (abbreviation, body) in SpecBuiltIns)
        {
            var template = builtIns.Single(t => t.Abbreviation == abbreviation);
            Assert.Equal(body, template.Body);
            Assert.Equal("pascal", template.Language);
            Assert.False(string.IsNullOrWhiteSpace(template.Description), abbreviation);
        }
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-060")]
    [Trait("Req", "IDE-ED-065")]
    [Trait("Req", "IDE-ED-062")]
    [Trait("Req", "IDE-ED-066")]
    [Trait("Req", "IDE-SET-080")]
    public async Task ExpansionFieldsAndMirrors()
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
    public async Task TabExpansionRules()
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
}
