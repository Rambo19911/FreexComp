using System.Globalization;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

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
    public async Task JumpsAndTogglesImplementation()
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
}
