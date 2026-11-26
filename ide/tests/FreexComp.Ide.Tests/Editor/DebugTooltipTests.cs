using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>
/// Value tooltips while the program is paused (I-145; editor.md UI-ED-130, the debug-session interface §6.1): the hover
/// tooltip of an identifier shows <c>= value</c> from <c>EvaluateAsync(…, EvaluateContext.Hover)</c>; what cannot be
/// evaluated shows nothing — with a session over the scripted adapter and the demo program's memory.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class DebugTooltipTests
{
    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-ED-130")]
    [Trait("Req", "DBG-258")]
    public async Task HoverEvaluatesIdentifier()
    {
        await using var h = await DebugScenario.StartDemoAsync();
        var demo = DebugScenario.DemoPath(h);
        var settings = h.Services.Settings;
        h.DebugSessions.UseScenario($$"""
            { {{DebugDemo.Data()}}, "steps": [
              {{DebugDemo.FirstStep(demo)}},
              {{DebugDemo.AtStop(demo)}},
              {{DebugScenarios.Continue()}} ] }
            """);
        Assert.True(settings.Get(SettingsCatalog.DebuggerEvaluateTooltips));
        var delay = TimeSpan.FromMilliseconds(settings.Get(SettingsCatalog.EditorHoverDelayMs));
        var (document, editor, _) = await DebugScenario.OpenAsync(h, demo);
        var view = editor.FindAncestorOfType<CodeEditorView>()!;
        var hover = view.GetVisualDescendants().OfType<HoverView>().Single();
        var textView = editor.TextArea.TextView;
        var text = editor.Document.Text;
        var stopLine = editor.Document.GetLineByNumber(DebugDemo.StopLine); // "  WriteLn(Total);"
        var total = stopLine.Offset + editor.Document.GetText(stopLine).IndexOf("Total", StringComparison.Ordinal);
        var writeLn = stopLine.Offset + editor.Document.GetText(stopLine).IndexOf("WriteLn", StringComparison.Ordinal);

        void PointTo(int offset)
        {
            textView.EnsureVisualLines();
            var position = document.Buffer.GetPosition(offset);
            var at = textView.GetVisualPosition(new TextViewPosition(position.Line, position.Column), VisualYPosition.LineMiddle) - textView.ScrollOffset;
            h.Window.MouseMove(textView.TranslatePoint(at + new Point(textView.WideSpaceWidth / 2, 0), h.Window)!.Value);
            Dispatcher.UIThread.RunJobs();
        }

        // Rests on the identifier for the hover delay, then waits for the answers (the language service's, the debugger's).
        async Task RestOnAsync(int offset)
        {
            h.Window.MouseMove(new Point(2, 2)); // away first: a new hover
            Dispatcher.UIThread.RunJobs();
            h.Clock.Advance(TimeSpan.FromSeconds(1));
            PointTo(offset);
            h.Clock.Advance(delay);
            await document.Hover!.Pending;
            Dispatcher.UIThread.RunJobs();
        }

        // Not debugging: no value line.
        editor.TextArea.Caret.Offset = total;
        editor.ScrollToLine(DebugDemo.StopLine);
        Dispatcher.UIThread.RunJobs();
        await RestOnAsync(total + 1);
        Assert.Null(hover.ValueLine);

        h.Press("F11");
        await DebugScenario.WaitForStopAsync(h, DebugDemo.StopLine);
        Assert.Contains("Total", text, StringComparison.Ordinal);

        // Paused: the tooltip of the variable shows "= <value>" as its first line.
        await RestOnAsync(total + 1);
        Assert.True(hover.IsVisible);
        Assert.Equal("= 24", hover.ValueLine);
        var valueLine = h.Find<TextBlock>("Editor.Hover.Value");
        Assert.Equal("= 24", valueLine.Text);
        Assert.Same(valueLine, hover.GetVisualDescendants().OfType<TextBlock>().First());
        Assert.Contains("codeSmall", valueLine.Classes);
        Assert.Equal((total, 5), (document.Hover!.Current!.Offset, document.Hover.Current.Length));
        Assert.True(editor.TextArea.IsKeyboardFocusWithin || h.FocusedAutomationId is not null); // it never takes focus away

        // What the debugger cannot evaluate (a routine, not a variable) shows no value — and no error.
        await RestOnAsync(writeLn + 2);
        Assert.Null(hover.ValueLine);
        Assert.Null(h.TryFind<TextBlock>("Editor.Hover.Value"));
        Assert.Empty(h.Messages.Requests);

        // debugger.evaluateTooltips = false: no value line.
        settings.Set(SettingsCatalog.DebuggerEvaluateTooltips, false);
        await RestOnAsync(total + 1);
        Assert.Null(hover.ValueLine);
        settings.Set(SettingsCatalog.DebuggerEvaluateTooltips, true);
        await RestOnAsync(total + 1);
        Assert.Equal("= 24", hover.ValueLine);

        // The program runs again: no value.
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        await RestOnAsync(total + 1);
        Assert.Null(hover.ValueLine);

        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        await DebugScenario.WaitForScenarioCompleteAsync(h); // I-301: the line may be read after the session ended
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());
    }

    /// <summary>
    /// DBG-258 (I-223): the tooltip evaluates the full designator up to the hovered token, determined from the syntax tree
    /// — <c>P.X</c> on <c>X</c>, <c>Nums[1]</c> on the closing bracket, <c>PTotal^</c> on the caret — and its value is
    /// expandable like a row of Locals: the fields of a record, the elements of an array.
    /// </summary>
    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Phase", "8")]
    [Trait("Req", "DBG-258")]
    public async Task HoverEvaluatesFullDesignatorExpandable()
    {
        // The last statement indexes the array and dereferences the pointer (same lines as the program the type information describes).
        await using var h = await DebugScenario.StartDemoAsync(o =>
            o.InitialFiles[@"Hello\DbgDemo.pas"] = DebugDemo.SourceText.Replace("WriteLn('done');", "WriteLn(Nums[1], PTotal^);", StringComparison.Ordinal));
        var demo = DebugScenario.DemoPath(h);
        h.DebugSessions.UseScenario($$"""
            { {{DebugDemo.Data()}}, "steps": [
              {{DebugDemo.FirstStep(demo)}},
              {{DebugDemo.AtStop(demo)}},
              {{DebugScenarios.Continue()}} ] }
            """);
        var delay = TimeSpan.FromMilliseconds(h.Services.Settings.Get(SettingsCatalog.EditorHoverDelayMs));
        var (document, editor, _) = await DebugScenario.OpenAsync(h, demo);
        var view = editor.FindAncestorOfType<CodeEditorView>()!;
        var hover = view.GetVisualDescendants().OfType<HoverView>().Single();
        var textView = editor.TextArea.TextView;
        int At(int line, string text, int delta = 0)
        {
            var row = editor.Document.GetLineByNumber(line);
            var column = editor.Document.GetText(row).IndexOf(text, StringComparison.Ordinal);
            Assert.True(column >= 0, text);
            return row.Offset + column + delta;
        }

        async Task RestOnAsync(int offset)
        {
            h.Window.MouseMove(new Point(2, 2));
            Dispatcher.UIThread.RunJobs();
            h.Clock.Advance(TimeSpan.FromSeconds(1));
            editor.ScrollTo(document.Buffer.GetPosition(offset).Line, 1);
            textView.EnsureVisualLines();
            var position = document.Buffer.GetPosition(offset);
            var at = textView.GetVisualPosition(new TextViewPosition(position.Line, position.Column), VisualYPosition.LineMiddle) - textView.ScrollOffset;
            h.Window.MouseMove(textView.TranslatePoint(at + new Point(textView.WideSpaceWidth / 2, 0), h.Window)!.Value);
            Dispatcher.UIThread.RunJobs();
            h.Clock.Advance(delay);
            await document.Hover!.Pending;
            Dispatcher.UIThread.RunJobs();
        }

        async Task ExpandAsync(string expanderId)
        {
            var node = document.Hover!.Current!.ValueNode!;
            h.Click(expanderId);
            await h.WaitForAsync(() => AllNodes(node).All(n => n.Loading.IsCompleted), "the children read");
            await DebugScenario.SettleAsync(h);
        }

        static IEnumerable<Core.Debugging.HoverValueNode> AllNodes(Core.Debugging.HoverValueNode node) =>
            [node, .. (node.Children ?? []).SelectMany(AllNodes)];

        string Text(string id) => h.Find<TextBlock>(id).Text ?? "";

        h.Press("F11");
        await DebugScenario.WaitForStopAsync(h, DebugDemo.StopLine);

        // On X of "P.X := 3": the field.
        var x = At(62, "X :=");
        await RestOnAsync(x);
        Assert.Equal("= 3", hover.ValueLine);
        Assert.Equal((x, 1), (document.Hover!.Current!.Offset, document.Hover.Current.Length)); // the hovered token
        Assert.Null(h.TryFind<Control>("Editor.Hover.Value.Expander")); // an Integer is a leaf

        // On the closing bracket of "Nums[1]", on the caret of "PTotal^".
        await RestOnAsync(At(73, "]"));
        Assert.Equal("= 20", hover.ValueLine);
        await RestOnAsync(At(73, "^"));
        Assert.Equal("= 24", hover.ValueLine);

        // On P of "P.X": the record, expandable; its fields as lines "Name = value" with the keys of UI-TW-1007.
        await RestOnAsync(At(62, "P.X"));
        Assert.Equal("= (X: 3; Y: 4)", hover.ValueLine);
        await ExpandAsync("Editor.Hover.Value.Expander");
        Assert.True(hover.IsVisible);
        Assert.Equal(("X = 3", "Y = 4"), (Text("Editor.Hover.Row.P.X"), Text("Editor.Hover.Row.P.Y")));
        h.Click("Editor.Hover.Value.Expander"); // collapsed again: the lines go
        Assert.Null(h.TryFind<Control>("Editor.Hover.Row.P.X"));

        // Nums: its elements as lines "[n] = value" (more than 100 come in ranges of 100: HoverValueNodeTests).
        await RestOnAsync(At(64, "Nums"));
        await ExpandAsync("Editor.Hover.Value.Expander");
        Assert.Equal(["[0]", "[1]", "[2]"], document.Hover!.Current!.ValueNode!.Children!.Select(c => c.Name));
        Assert.Equal(("[0] = 10", "[2] = 30"), (Text("Editor.Hover.Row.Nums[0]"), Text("Editor.Hover.Row.Nums[2]")));
        Assert.Empty(h.Messages.Requests);

        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        await DebugScenario.WaitForScenarioCompleteAsync(h);
    }
}
