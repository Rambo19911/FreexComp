using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

public sealed partial class RenameDialogTests
{
    /// <summary>
    /// IDE-REFAC-062, UI-DLG-384 (I-385): before the rename the dialog counts the occurrences that will not be changed —
    /// here two in inactive code of Unit2 — in <c>Notes</c>, below the summary.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-062")]
    [Trait("Req", "UI-DLG-384")]
    public async Task NotesCountUnrenamedOccurrences()
    {
        var unit2 = Unit2.Replace("  Log(S);\r\n", "  Log(S);\r\n  {$IFDEF NEVER_DEFINED}\r\n  S := C.GetName + C.GetName;\r\n  {$ENDIF}\r\n", StringComparison.Ordinal);
        var (h, editor, doc) = await StartAsync(o => o.InitialFiles[@"Hello\Unit2.pas"] = unit2);
        await using var _ = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            CaretOn(editor, doc, "GetName");
            var (dlg, model) = await F2Async(h);
            Assert.False(dlg.Find<TextBlock>("Notes").IsVisible);

            Replace(dlg, "FullName");
            await SummaryAsync(h, dlg, "5 changes in 3 files");

            var notes = dlg.Find<TextBlock>("Notes");
            Assert.True(notes.IsVisible);
            Assert.Equal("Occurrences that will not be changed (inactive or unresolved code): 2", Text(dlg, "Notes"));
            Assert.Equal("Dialog.RenameSymbol.Notes", AutomationIds.Get(notes));
            await CancelAsync(h, dlg, model);
        });
    }
}
