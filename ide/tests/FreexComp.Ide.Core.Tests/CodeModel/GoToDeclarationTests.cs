using System.Globalization;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.CodeModel;

/// <summary>Go to Declaration (I-117, IDE-ED-101) over the project <c>Compilation</c>.</summary>
[Trait("Phase", "3")]
public sealed class GoToDeclarationTests
{
    private const string Unit = "unit Unit1;\ninterface\nprocedure Run;\nimplementation\nvar\n  Count: Integer;\nprocedure Run;\nbegin\n  Count := Zork;\nend;\nend.\n";
    private const string Main = "program Hello;\nuses Unit1;\nbegin\n  Run;\nend.\n";

    private static async Task<BuildTestBed> StartAsync()
    {
        var bed = new BuildTestBed();
        var src = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        foreach (var file in Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json")).Where(File.Exists))
            bed.File(@"G:\rtl\" + Path.GetFileName(file), File.ReadAllText(file));
        await bed.OpenHelloAsync();
        Assert.True(await bed.Services.Documents.CloseAllAsync()); // the project opened its main source with the default text
        bed.File(BuildTestBed.UnitPath, Unit);
        bed.File(BuildTestBed.MainPath, Main);
        return bed;
    }

    /// <summary>Puts the caret at the start of the <paramref name="occurrence"/>-th <paramref name="text"/> and runs F12's command.</summary>
    private static async Task GoAsync(BuildTestBed bed, TextDocumentViewModel doc, string text, int occurrence = 1)
    {
        var offset = -1;
        for (var i = 0; i < occurrence; i++)
            offset = doc.Buffer.Text.IndexOf(text, offset + 1, StringComparison.Ordinal);
        bed.Services.Documents.Activate(doc);
        doc.Selection = TextSelection.At(offset + 1); // inside the identifier
        Assert.True(bed.Services.Commands.CanExecute("search.goToDeclaration"));
        await bed.Services.Commands.ExecuteAsync("search.goToDeclaration");
    }

    private static (string? Path, TextPosition Caret) Where(BuildTestBed bed) =>
        (bed.Services.Documents.ActiveDocument?.FilePath, ((TextDocumentViewModel)bed.Services.Documents.ActiveDocument!).ViewState.Caret);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-101")]
    public Task JumpsAndTogglesImplementation() => TestUiThread.RunAsync(() => JumpsAndTogglesImplementationOnUiThread()); // I-209

    private async Task JumpsAndTogglesImplementationOnUiThread()
    {
        var bed = await StartAsync();
        var unit = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        var statuses = new List<string?>();
        bed.Services.Status.Changed += (_, _) => statuses.Add(bed.Services.Status.Message);

        // A variable reference → its declaring identifier.
        await GoAsync(bed, unit, "Count", occurrence: 2);
        Assert.Equal((BuildTestBed.UnitPath, new TextPosition(6, 3)), Where(bed));

        // Interface heading → implementation heading → back to the declaration.
        await GoAsync(bed, unit, "Run", occurrence: 1);
        Assert.Equal((BuildTestBed.UnitPath, new TextPosition(7, 11)), Where(bed));
        await GoAsync(bed, unit, "Run", occurrence: 2);
        Assert.Equal((BuildTestBed.UnitPath, new TextPosition(3, 11)), Where(bed));

        // From another file: the call opens the unit at the routine; the uses entry opens it at line 1.
        var main = await bed.OpenDocumentAsync(BuildTestBed.MainPath);
        await GoAsync(bed, main, "Run");
        Assert.Equal((BuildTestBed.UnitPath, new TextPosition(7, 11)), Where(bed));
        await GoAsync(bed, main, "Unit1");
        Assert.Equal((BuildTestBed.UnitPath, new TextPosition(1, 1)), Where(bed));

        // No symbol, not an identifier: status messages, the caret stays.
        await GoAsync(bed, unit, "Zork");
        Assert.Equal(BuildTestBed.UnitPath, Where(bed).Path);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Editor_NoDeclaration, "Zork"), statuses[^1]);
        await GoAsync(bed, unit, "begin");
        Assert.Equal((BuildTestBed.UnitPath, new TextPosition(8, 2)), Where(bed)); // where the caret was put
        Assert.Equal(Strings.Msg_Editor_NotAnIdentifier, statuses[^1]);
    }

    /// <summary>
    /// F02-004 acceptance 2 (I-232): Go to Declaration on a library symbol — <c>TStringList.Add</c> of the real run-time
    /// library — opens the real source file, which lies under a root of <c>ILibrarySourceService</c>, as a protected
    /// document.
    /// </summary>
    [Fact(Timeout = 60000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-ED-101")]
    [Trait("Req", "IDE-LIB-001")]
    [Trait("Req", "IDE-LIB-002")]
    public Task LibrarySymbolOpensFileUnderLibraryRoot() => TestUiThread.RunAsync(async () =>
    {
        var bed = new BuildTestBed();
        foreach (var file in Directory.GetFiles(Path.Combine(SpecTables.RepoRootPath(), "rtl", "src")))
            bed.File(@"G:\rtl\" + Path.GetFileName(file), File.ReadAllText(file));
        await bed.OpenHelloAsync();
        Assert.True(await bed.Services.Documents.CloseAllAsync());
        bed.File(BuildTestBed.MainPath, "program Hello;\nuses Classes;\nvar\n  L: TStringList;\nbegin\n  L := TStringList.Create;\n  L.Add('x');\n  L.Free;\nend.\n");
        var main = await bed.OpenDocumentAsync(BuildTestBed.MainPath);

        await GoAsync(bed, main, "Add");

        var library = bed.Services.LibrarySource;
        Assert.Equal([@"G:\rtl"], library.Roots);
        var target = Assert.IsType<TextDocumentViewModel>(bed.Services.Documents.ActiveDocument);
        Assert.Equal(@"G:\rtl\Classes.pas", target.FilePath);
        Assert.Contains(library.Roots, root => target.FilePath!.StartsWith(root + @"\", StringComparison.OrdinalIgnoreCase));
        Assert.True(library.IsLibrarySource(target.FilePath!));
        Assert.Equal(File.ReadAllText(Path.Combine(SpecTables.RepoRootPath(), "rtl", "src", "Classes.pas")).ReplaceLineEndings("\n"),
            target.Buffer.Text.ReplaceLineEndings("\n")); // the real source, not a stub
        var line = target.Buffer.GetLine(target.ViewState.Caret.Line);
        Assert.Contains("TStringList.Add(const S: string)", target.Buffer.Text.Substring(line.Offset, line.Length), StringComparison.Ordinal);
        Assert.True(target.IsLibraryProtected);
        Assert.True(target.Buffer.IsReadOnly);
        Assert.False(main.IsLibraryProtected);
    });

    /// <summary>
    /// F05-099 acceptance 2 (IDE-LIB-039): Go to Declaration on a UI-framework symbol — <c>TButton</c> of the real
    /// framework sources — opens the file of its unit under the framework root of <c>ILibrarySourceService.Roots</c>, as a
    /// protected document. The file's name is taken from the result.
    /// </summary>
    [Fact(Timeout = 120000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-LIB-039")]
    public Task FrameworkSymbolOpensProtectedFileUnderFrameworkRoot() => TestUiThread.RunAsync(async () =>
    {
        var bed = new BuildTestBed();
        var repo = SpecTables.RepoRootPath();
        foreach (var (from, to) in ((string, string)[])[(@"rtl\src", @"G:\rtl"), (@"rtl\winapi\src", @"G:\winapi\src"), (@"ui-framework\src", @"G:\ui\src")])
        {
            foreach (var file in Directory.GetFiles(Path.Combine(repo, from)))
                bed.File(Path.Combine(to, Path.GetFileName(file)), File.ReadAllText(file));
        }

        bed.Services.Settings.Set(Core.Settings.SettingsCatalog.ToolchainFrameworkDir, @"G:\ui");
        await bed.OpenHelloAsync(appType: "gui");
        Assert.True(await bed.Services.Documents.CloseAllAsync());
        bed.File(BuildTestBed.MainPath, "program Hello;\nuses Forms, StdCtrls;\nvar\n  B: TButton;\nbegin\n  B := nil;\nend.\n");
        var main = await bed.OpenDocumentAsync(BuildTestBed.MainPath);

        await GoAsync(bed, main, "TButton");

        var library = bed.Services.LibrarySource;
        Assert.Equal([@"G:\rtl", @"G:\ui", @"G:\winapi\src"], library.Roots);
        var target = Assert.IsType<TextDocumentViewModel>(bed.Services.Documents.ActiveDocument);
        Assert.NotSame(main, target);
        Assert.StartsWith(@"G:\ui\", target.FilePath!, StringComparison.OrdinalIgnoreCase); // under the framework root
        Assert.True(library.IsLibrarySource(target.FilePath!));
        Assert.Equal(File.ReadAllText(Path.Combine(repo, "ui-framework", "src", Path.GetFileName(target.FilePath!))).ReplaceLineEndings("\n"),
            target.Buffer.Text.ReplaceLineEndings("\n")); // the real source
        var line = target.Buffer.GetLine(target.ViewState.Caret.Line);
        Assert.Contains("TButton = class", target.Buffer.Text.Substring(line.Offset, line.Length), StringComparison.Ordinal);
        Assert.True(target.IsLibraryProtected);
        Assert.True(target.Buffer.IsReadOnly);
    });
}
