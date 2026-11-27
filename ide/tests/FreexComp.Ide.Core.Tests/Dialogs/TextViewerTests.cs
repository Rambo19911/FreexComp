using System.Globalization;
using System.Reflection;
using System.Text;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Debugging;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Dialogs;

/// <summary>
/// The View Text command and the Text Viewer dialog (I-310; card F14-013, visualizers.md §5: UI-TW-1008, UI-TW-1108,
/// UI-DLG-323…327, 329): the command is enabled for exactly one paused row with a text handle (Locals, Watches, the
/// Evaluate dialog's button); the dialog shows the debugger's details, loads the text in chunks of 65 536 elements at the
/// reported <c>Next</c> with progress, stops at the cap or a fault, shows hex rows, copies the whole text and cancels the
/// reading when it closes. A fake debug service answers the text calls; the command test runs a scripted session over the
/// demo program's memory.
/// </summary>
[Trait("Phase", "8")]
public sealed class TextViewerTests
{
    private const ulong Payload = 0x000001F3A2B40010;

    /// <summary>A paused debug service whose text calls the test answers; every call is recorded.</summary>
    public class TextSource : DispatchProxy
    {
        public Func<CancellationToken, Task<TextDetails>> Details { get; set; } = _ => throw new InvalidOperationException("no details");

        public Func<long, int, CancellationToken, Task<TextChunk>> Read { get; set; } = (_, _, _) => throw new InvalidOperationException("no text");

        public Func<ulong, int, Task<byte[]>> Memory { get; set; } = (_, _) => throw new InvalidOperationException("no memory");

        public int DetailsCalls { get; private set; }

        public List<(long Start, int Count)> Reads { get; } = [];

        public List<(ulong Address, int Count)> MemoryReads { get; } = [];

        /// <summary>Called before each text read is answered.</summary>
        public Action<long>? BeforeRead { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "get_" + nameof(IDebugService.IsPaused):
                    return true;
                case nameof(IDebugService.Track):
                    return args![0];
                case nameof(IDebugService.GetTextDetailsAsync):
                    DetailsCalls++;
                    return Details((CancellationToken)args![1]!);
                case nameof(IDebugService.ReadTextAsync):
                    var start = (long)args![1]!;
                    var count = (int)args[2]!;
                    BeforeRead?.Invoke(start);
                    Reads.Add((start, count));
                    return Read(start, count, (CancellationToken)args[3]!);
                case nameof(IDebugService.ReadMemoryAsync):
                    MemoryReads.Add(((ulong)args![0]!, (int)args[1]!));
                    return Memory((ulong)args[0]!, (int)args[1]!);
                default:
                    throw new NotSupportedException(targetMethod.Name);
            }
        }
    }

    private static (IDebugService Debug, TextSource Source) Fake()
    {
        var debug = DispatchProxy.Create<IDebugService, TextSource>();
        return (debug, (TextSource)(object)debug);
    }

    private static VariableInfo Row(string name, string type = "string") =>
        new(name, "'…'", type, ValueKind.String, null, 0, false, name, null) { Text = new VariableHandle(1, 7) };

    private static TextDetails Unicode(long length, int? refCount = 1) => new(TextKind.Unicode, length, refCount, 1200, "UTF-16", 2, Payload, null);

    private static string N(long n) => n.ToString("N0", CultureInfo.CurrentCulture);

    private static string Format(string text, params object[] args) => string.Format(CultureInfo.CurrentCulture, text, args);

    /// <summary>Answers reads from <paramref name="text"/> (one element per character), at most <c>count</c> elements.</summary>
    private static Func<long, int, CancellationToken, Task<TextChunk>> From(string text) => (start, count, _) =>
    {
        var length = (int)Math.Min(count, Math.Max(0, text.Length - start));
        var next = start + length;
        return Task.FromResult(new TextChunk(start, next, text.Substring((int)start, length), next >= text.Length, false));
    };

    private static TextViewerViewModel Open(IDebugService debug, FakeClipboard? clipboard = null, Layout.ILayoutService? layout = null, string name = "Greeting", string type = "string") =>
        new(debug, clipboard ?? new FakeClipboard(), layout, name, Row(name, type));

    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Category", "Integration")]
    [Trait("Req", "UI-TW-1008")]
    [Trait("Req", "UI-TW-1108")]
    [Trait("Req", "UI-DLG-323")]
    public Task OpensForRowsWithText_DisabledOtherwise() => TestUiThread.RunAsync(OpensForRowsWithTextOnUiThread);

    private static async Task OpensForRowsWithTextOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        var demo = bed.File(Path.Combine(bed.Dir, "DbgDemo.pas"), DebugDemo.SourceText);
        bed.File(Path.ChangeExtension(bed.Exe, ".pdi"), DebugDemo.TypeInfo(demo));
        bed.Sessions.UseScenario($$"""
            { {{DebugDemo.Data()}}, "steps": [
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("pause")}} ] },
              {{DebugDemo.AtStop(demo)}} ] }
            """);
        var commands = bed.Services.Commands;
        var shell = bed.Services.DebugShell!;
        bed.Debug.Watches.Add("S");
        bed.Debug.Watches.Add("Total");
        Assert.False(commands.CanExecute("locals.viewText")); // no session

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Debug.Stopped += (_, _) => stopped.TrySetResult();
        Assert.True(await bed.Debug.StartAsync());
        await DebugTestBed.WaitAsync(stopped.Task, "the stop");
        await DebugTestBed.WaitAsync(bed.Debug.WhenViewsSettledAsync(), "the debug views' requests");

        // UI-TW-1008: the context menu of Locals; the command is enabled for one row whose value has a text handle.
        Assert.Equal(["locals.modifyValue", "locals.copyValue", "locals.viewText", null, "run.addWatch", "run.evaluate"], shell.Locals.ContextCommands);
        shell.Locals.SelectedKeys = ["S"];
        Assert.True(commands.CanExecute("locals.viewText"));
        foreach (var key in (string[])["Total", "P", "Nums", "Pet"])
        {
            shell.Locals.SelectedKeys = [key];
            Assert.False(commands.CanExecute("locals.viewText"), key);
        }

        shell.Locals.SelectedKeys = [];
        Assert.False(commands.CanExecute("locals.viewText"));

        // It opens the Text Viewer for the row: the text comes from the session, chunk by chunk.
        var shown = new List<TextViewerViewModel>();
        bed.Dialogs.Script(TextViewerViewModel.DialogName, async d =>
        {
            var viewer = (TextViewerViewModel)d;
            shown.Add(viewer);
            await DebugTestBed.WaitAsync(viewer.Loading, "the text");
            await d.PressCancelAsync();
        });
        shell.Locals.SelectedKeys = ["S"];
        await commands.ExecuteAsync("locals.viewText");
        var fromLocals = Assert.Single(shown);
        Assert.Equal(Format(Strings.TextViewer_Title, "S"), fromLocals.Title);
        Assert.Equal("Héllo", fromLocals.Text);
        Assert.StartsWith("string · " + Format(Strings.TextViewer_Length, N(5)) + " · ", fromLocals.Details, StringComparison.Ordinal);
        Assert.Equal("", fromLocals.Status);

        // UI-TW-1108: Watches — the same command on the same condition (the window with focus acts).
        Assert.Equal(["watches.add", "watches.edit", "watches.delete", "watches.deleteAll", null, "locals.modifyValue", "locals.copyValue", "locals.viewText", null, "run.evaluate"],
            shell.Watches.ContextCommands);
        shell.Watches.IsActive = true;
        shell.Watches.SelectedKeys = ["1"]; // Total
        Assert.False(commands.CanExecute("locals.viewText"));
        shell.Watches.SelectedKeys = ["0", "1"];
        Assert.False(commands.CanExecute("locals.viewText"));
        shell.Watches.SelectedKeys = ["0"]; // S
        Assert.True(commands.CanExecute("locals.viewText"));
        await commands.ExecuteAsync("locals.viewText");
        Assert.Equal(2, shown.Count);
        Assert.Equal(("Héllo", Format(Strings.TextViewer_Title, "S")), (shown[1].Text, shown[1].Title));
        shell.Watches.IsActive = false;

        // UI-DLG-323: the Evaluate dialog's View Text button, right of Add Watch, enabled after an evaluation with a text.
        var evaluate = new EvaluateViewModel(bed.Debug, null, "S");
        Assert.Equal(["AddWatch", "ViewText", "Close"], evaluate.Buttons.Select(b => b.Id));
        Assert.True(evaluate.ViewText.IsAuxiliary);
        Assert.Equal(Strings.Evaluate_ViewText, evaluate.ViewText.AccessLabel);
        await DebugTestBed.WaitAsync(evaluate.Evaluation, "the evaluation of S");
        Assert.True(evaluate.ViewText.IsEnabled);
        evaluate.Expression = "Total";
        Assert.True(evaluate.ViewText.IsEnabled); // the last evaluation is still that of S
        await evaluate.ViewText.PressAsync();
        Assert.Equal(3, shown.Count);
        Assert.Equal(("Héllo", Format(Strings.TextViewer_Title, "S")), (shown[2].Text, shown[2].Title));
        await DebugTestBed.WaitAsync(evaluate.EvaluateAsync(), "the evaluation of Total");
        Assert.False(evaluate.ViewText.IsEnabled);
        evaluate.Expression = "Foo";
        await DebugTestBed.WaitAsync(evaluate.EvaluateAsync(), "a failed evaluation");
        Assert.False(evaluate.ViewText.IsEnabled);

        // Not paused (the session ended): disabled.
        shell.Locals.SelectedKeys = ["S"];
        await bed.Debug.ShutdownAsync();
        Assert.False(commands.CanExecute("locals.viewText"));
        Assert.Equal(3, shown.Count);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-323")]
    [Trait("Req", "UI-DLG-329")]
    public async Task HeaderShowsKindLengthReferenceCountEncoding()
    {
        // Unicode heap string: type · length (grouped) · reference count · encoding · payload address; Details asked once.
        var (debug, source) = Fake();
        source.Details = _ => Task.FromResult(Unicode(1_048_576));
        source.Read = (start, _, _) => Task.FromResult(new TextChunk(start, 1_048_576, "", true, false));
        var viewer = Open(debug);
        await viewer.Loading;
        Assert.Equal(Format(Strings.TextViewer_Title, "Greeting"), viewer.Title);
        Assert.Equal(1, source.DetailsCalls);
        Assert.Equal($"string · {Format(Strings.TextViewer_Length, N(1_048_576))} · {Format(Strings.TextViewer_RefCount, N(1))} · UTF-16 · $000001F3A2B40010", viewer.Details);
        Assert.Equal((720.0, 480.0, true, (400.0, 260.0)), (viewer.Width, viewer.Height ?? 0, viewer.CanResize, viewer.MinimumSize!.Value));

        // A literal (reference count -1), a length that is unknown (-1), a short string (maximum length, no reference
        // count), a computed string (no reference count, no address): parts without a value are left out.
        (TextDetails Details, string Type, string Expected)[] cases =
        [
            (Unicode(5, refCount: -1), "string", $"string · {Format(Strings.TextViewer_Length, N(5))} · {Strings.TextViewer_RefCountConstant} · UTF-16 · $000001F3A2B40010"),
            (new(TextKind.PChar, -1, null, null, "UTF-16", 2, 0x10, null), "PChar", $"PChar · {Strings.TextViewer_LengthUnknown} · UTF-16 · $0000000000000010"),
            (new(TextKind.Short, 5, null, null, "UTF-8", 1, 0x21, 255), "ShortString",
                $"ShortString · {Format(Strings.TextViewer_Length, N(5))} · {Format(Strings.TextViewer_MaxLength, N(255))} · UTF-8 · $0000000000000021"),
            (new(TextKind.Computed, 4, null, null, "UTF-16", 2, null, null), "string", $"string · {Format(Strings.TextViewer_Length, N(4))} · UTF-16"),
            (new(TextKind.Ansi, 7, 2, 1252, "Windows-1252 (1252)", 1, 0x40, null), "AnsiString",
                $"AnsiString · {Format(Strings.TextViewer_Length, N(7))} · {Format(Strings.TextViewer_RefCount, N(2))} · Windows-1252 (1252) · $0000000000000040"),
        ];
        foreach (var (details, type, expected) in cases)
        {
            (debug, source) = Fake();
            source.Details = _ => Task.FromResult(details);
            source.Read = (start, _, _) => Task.FromResult(new TextChunk(start, start, "", true, false));
            viewer = Open(debug, type: type);
            await viewer.Loading;
            Assert.Equal(expected, viewer.Details);
        }

        // UI-DLG-323: the details cannot be read — the message in Status (status.error), an empty text, nothing read.
        (debug, source) = Fake();
        source.Details = _ => Task.FromException<TextDetails>(new DebuggerException(DebugErrorCode.StaleReference, "The value is no longer valid."));
        viewer = Open(debug);
        await viewer.Loading;
        Assert.Equal(("The value is no longer valid.", true, "", false), (viewer.Status, viewer.StatusIsError, viewer.Text, viewer.IsLoading));
        Assert.Equal("string", viewer.Details);
        Assert.Empty(source.Reads);

        // UI-DLG-329: the text is what the debugger supplied, joined verbatim (#0, an unpaired surrogate, line ends).
        (debug, source) = Fake();
        const string Raw = "a\0b\uD800c\r\nd";
        source.Details = _ => Task.FromResult(Unicode(Raw.Length));
        source.Read = (start, count, _) => From(Raw)(start, Math.Min(count, 3), default);
        viewer = Open(debug);
        await viewer.Loading;
        Assert.Equal(Raw, viewer.Text);
        Assert.Equal([0L, 3, 6], source.Reads.Select(r => r.Start));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-324")]
    [Trait("Req", "UI-DLG-327")]
    public async Task LoadsInChunksUntilTheEnd_ShowsProgress()
    {
        // Card F14-013 acceptance 2 (IDE side): 1 048 576 units are read in 16 chunks of 65 536 at the reported Next; each
        // chunk is shown before the next is asked for, and the status says how far the loading is.
        const int Length = 1_048_576;
        var (debug, source) = Fake();
        var gate = new TaskCompletionSource<TextDetails>();
        source.Details = _ => gate.Task;
        source.Read = (start, count, _) =>
        {
            var text = new string((char)('a' + (start / 65536)), count);
            return Task.FromResult(new TextChunk(start, start + count, text, start + count >= Length, false));
        };
        var seen = new List<(int Shown, string Status, bool CopyEnabled)>();
        var viewer = Open(debug);
        source.BeforeRead = _ => seen.Add((viewer.Text.Length, viewer.Status, viewer.Copy.IsEnabled));
        gate.SetResult(Unicode(Length)); // the reads follow one another as each answer arrives
        await viewer.Loading;

        Assert.Equal(Enumerable.Range(0, 16).Select(i => ((long)i * 65536, 65536)), source.Reads);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (i * 65536, Format(Strings.Msg_Debug_TextLoading, N(i * 65536L), N(Length)), false)), seen);
        Assert.Equal(Length, viewer.Text.Length);
        Assert.Equal(string.Concat(Enumerable.Range(0, 16).Select(i => new string((char)('a' + i), 65536))), viewer.Text);
        Assert.Equal(("", false, false, true), (viewer.Status, viewer.StatusIsError, viewer.IsLoading, viewer.Copy.IsEnabled));

        // The status before the first chunk; chunks continue at the reported Next (a chunk may end early, DBG-519).
        (debug, source) = Fake();
        source.Details = _ => Task.FromResult(Unicode(5));
        var first = new TaskCompletionSource<TextChunk>();
        source.Read = (start, _, _) => start == 0 ? first.Task : Task.FromResult(new TextChunk(3, 5, "de", true, false));
        viewer = Open(debug);
        Assert.Equal((Format(Strings.Msg_Debug_TextLoading, N(0), N(5)), true, false), (viewer.Status, viewer.IsLoading, viewer.Copy.IsEnabled));
        first.SetResult(new TextChunk(0, 3, "abc", false, false));
        await viewer.Loading;
        Assert.Equal([(0L, 65536), (3L, 65536)], source.Reads);
        Assert.Equal("abcde", viewer.Text);

        // A length that is unknown (a pointer without a terminator in reach): no total.
        (debug, source) = Fake();
        source.Details = _ => Task.FromResult(new TextDetails(TextKind.PChar, -1, null, null, "UTF-16", 2, 0x10, null));
        var pending = new TaskCompletionSource<TextChunk>();
        source.Read = (start, _, _) => start == 0 ? Task.FromResult(new TextChunk(0, 4, "abcd", false, false)) : pending.Task;
        viewer = Open(debug);
        Assert.Equal(Format(Strings.Msg_Debug_TextLoadingNoLength, N(4)), viewer.Status);
        pending.SetResult(new TextChunk(4, 4, "", true, false));
        await viewer.Loading;
        Assert.Equal("", viewer.Status);

        // 1-byte kinds count bytes (Start/Next are byte offsets): the progress says bytes.
        (debug, source) = Fake();
        source.Details = _ => Task.FromResult(new TextDetails(TextKind.Ansi, 7, 1, 65001, "UTF-8 (65001)", 1, 0x40, null));
        pending = new TaskCompletionSource<TextChunk>();
        source.Read = (start, _, _) => start == 0 ? Task.FromResult(new TextChunk(0, 4, "Grü", false, false)) : pending.Task;
        viewer = Open(debug, type: "AnsiString");
        Assert.Equal(Format(Strings.Msg_Debug_TextLoadingBytes, N(4), N(7)), viewer.Status);
        pending.SetResult(new TextChunk(4, 7, "ße", true, false));
        await viewer.Loading;
        Assert.Equal(("Grüße", ""), (viewer.Text, viewer.Status));
        (debug, source) = Fake();
        source.Details = _ => Task.FromResult(new TextDetails(TextKind.PAnsiChar, -1, null, null, "8-bit", 1, 0x40, null));
        pending = new TaskCompletionSource<TextChunk>();
        source.Read = (start, _, _) => start == 0 ? Task.FromResult(new TextChunk(0, 2, "ab", false, false)) : pending.Task;
        viewer = Open(debug, type: "PAnsiChar");
        Assert.Equal(Format(Strings.Msg_Debug_TextLoadingBytesNoLength, N(2)), viewer.Status);
        pending.SetResult(new TextChunk(2, 2, "", true, false));
        await viewer.Loading;
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "UI-DLG-324")]
    [Trait("Req", "UI-DLG-325")]
    public async Task CapAndFaultEndStates()
    {
        // The cap: 16 777 216 characters shown, then Msg.Debug.TextCut; nothing more is read.
        var block = new string('x', 65536);
        var (debug, source) = Fake();
        source.Details = _ => Task.FromResult(Unicode(20_000_000));
        source.Read = (start, count, _) => Task.FromResult(new TextChunk(start, start + count, block[..count], false, false));
        var viewer = Open(debug);
        await viewer.Loading;
        Assert.Equal(256, source.Reads.Count);
        Assert.All(source.Reads, r => Assert.Equal(65536, r.Count));
        Assert.Equal(TextViewerViewModel.Cap, viewer.Text.Length);
        Assert.Equal((Format(Strings.Msg_Debug_TextCut, N(16_777_216)), false, false, true), (viewer.Status, viewer.StatusIsError, viewer.IsLoading, viewer.Copy.IsEnabled));

        // A fault: the text read so far stays; Msg.Debug.TextUnreadable with the debugger's message, as an error.
        (debug, source) = Fake();
        source.Details = _ => Task.FromResult(Unicode(10));
        source.Read = (start, _, _) => start == 0
            ? Task.FromResult(new TextChunk(0, 4, "abcd", false, false))
            : Task.FromException<TextChunk>(new DebuggerException(DebugErrorCode.MemoryUnreadable, "<unreadable memory at $0000000000001008>"));
        viewer = Open(debug);
        await viewer.Loading;
        Assert.Equal(("abcd", Format(Strings.Msg_Debug_TextUnreadable, "<unreadable memory at $0000000000001008>"), true, false, true),
            (viewer.Text, viewer.Status, viewer.StatusIsError, viewer.IsLoading, viewer.Copy.IsEnabled));

        // A chunk readable only in part (Truncated): the prefix stays; the message is the debugger's for the first unreadable element.
        (debug, source) = Fake();
        source.Details = _ => Task.FromResult(Unicode(10));
        source.Read = (start, _, _) => start == 0
            ? Task.FromResult(new TextChunk(0, 3, "abc", true, true))
            : Task.FromException<TextChunk>(new DebuggerException(DebugErrorCode.MemoryUnreadable, "<unreadable memory at $0000000000001006>"));
        viewer = Open(debug);
        await viewer.Loading;
        Assert.Equal(("abc", Format(Strings.Msg_Debug_TextUnreadable, "<unreadable memory at $0000000000001006>"), true),
            (viewer.Text, viewer.Status, viewer.StatusIsError));
        Assert.Equal([0L, 3], source.Reads.Select(r => r.Start));

        // The session went away under the dialog: as unreadable, with "not paused".
        (debug, source) = Fake();
        source.Details = _ => Task.FromResult(Unicode(10));
        source.Read = (_, _, _) => Task.FromException<TextChunk>(new ObjectDisposedException("session"));
        viewer = Open(debug);
        await viewer.Loading;
        Assert.Equal((Format(Strings.Msg_Debug_TextUnreadable, Strings.Msg_Debug_NotPaused), true), (viewer.Status, viewer.StatusIsError));

        // The whole text ends exactly at the cap: no cut message.
        (debug, source) = Fake();
        source.Details = _ => Task.FromResult(Unicode(TextViewerViewModel.Cap));
        source.Read = (start, count, _) => Task.FromResult(new TextChunk(start, start + count, block[..count], start + count >= TextViewerViewModel.Cap, false));
        viewer = Open(debug);
        await viewer.Loading;
        Assert.Equal((TextViewerViewModel.Cap, ""), (viewer.Text.Length, viewer.Status));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-326")]
    [Trait("Req", "UI-DLG-327")]
    public async Task WrapAndHex_CopyCopiesTheWholeText()
    {
        // Wrap: on by default, kept in the UI state dialogs.TextViewer.wrap.
        var layout = TestComposition.Build().Layout;
        var (debug, source) = Fake();
        const string Text = "Hi\0é\nline two, longer.";
        source.Details = _ => Task.FromResult(Unicode(Text.Length));
        var release = new TaskCompletionSource();
        source.Read = async (start, count, ct) =>
        {
            await release.Task;
            return await From(Text)(start, count, ct);
        };
        var clipboard = new FakeClipboard();
        var viewer = Open(debug, clipboard, layout);
        Assert.True(viewer.Wrap);
        viewer.Wrap = false;
        Assert.Equal("false", layout.GetDialogValue(TextViewerViewModel.DialogName, "wrap"));

        // UI-DLG-327: Copy is disabled while loading.
        Assert.Equal(["Copy", "Close"], viewer.Buttons.Select(b => b.Id));
        Assert.True(viewer.Copy.IsAuxiliary);
        Assert.Equal((true, true), (viewer.CloseButton.IsDefault, viewer.CloseButton.IsCancel));
        Assert.False(viewer.Copy.IsEnabled);
        release.SetResult();
        await viewer.Loading;
        Assert.True(viewer.Copy.IsEnabled);

        // UI-DLG-326: rows of an offset and 8 UTF-16 units, two spaces, the units as characters (control characters and #0 as '.').
        var display = new StringBuilder(viewer.DisplayText);
        viewer.DisplayChanged += (_, e) =>
        {
            display.Length = e.Keep;
            display.Append(e.Appended);
        };
        Assert.Equal(Text, viewer.DisplayText);
        viewer.Hex = true;
        await viewer.HexPending;
        string[] rows =
        [
            "00000000 0048 0069 0000 00E9 000A 006C 0069 006E  Hi.é.lin",
            "00000008 0065 0020 0074 0077 006F 002C 0020 006C  e two, l",
            "00000010 006F 006E 0067 0065 0072 002E            onger.",
        ];
        Assert.Equal(string.Join("\n", rows), viewer.DisplayText);
        Assert.Equal(viewer.DisplayText, display.ToString());

        // Switching keeps the element: the row of element 17 starts at the third line; back in text, element 16 is offset 16.
        var rowStart = viewer.DisplayOffsetOf(17);
        Assert.Equal(2 * (rows[0].Length + 1), rowStart);
        Assert.Equal(16, viewer.ElementAt(rowStart));
        viewer.Hex = false;
        Assert.Equal(Text, viewer.DisplayText);
        Assert.Equal(Text, display.ToString());
        Assert.Equal((16, 16L), (viewer.DisplayOffsetOf(16), viewer.ElementAt(16)));

        // Copy puts the whole loaded text on the clipboard, not the hex rows.
        viewer.Hex = true;
        await viewer.Copy.PressAsync();
        Assert.Equal(Text, clipboard.Text);
        Assert.False(new TextViewerViewModel(debug, clipboard, layout, "Again", Row("Again")).Wrap);

        // 1-byte kinds: the bytes come from ReadMemoryAsync(PayloadAddress + offset) — 16 per row, two hex digits each.
        (debug, source) = Fake();
        source.Details = _ => Task.FromResult(new TextDetails(TextKind.Ansi, 18, 1, 65001, "UTF-8 (65001)", 1, 0x1000, null));
        var bytes = Encoding.UTF8.GetBytes("Grüße\u0001 aus Köln");
        Assert.Equal(18, bytes.Length);
        source.Read = (start, count, _) => Task.FromResult(new TextChunk(start, 18, "Grüße\u0001 aus Köln", true, false));
        source.Memory = (address, count) => Task.FromResult(bytes[(int)(address - 0x1000)..((int)(address - 0x1000) + count)]);
        viewer = Open(debug, type: "AnsiString");
        await viewer.Loading;
        Assert.Empty(source.MemoryReads); // read for the hex rows only
        viewer.Hex = true;
        await viewer.HexPending;
        Assert.Equal([(0x1000ul, 18)], source.MemoryReads);
        Assert.Equal(
            "00000000 47 72 C3 BC C3 9F 65 01 20 61 75 73 20 4B C3 B6  Gr....e. aus K..\n" +
            "00000010 6C 6E                                            ln",
            viewer.DisplayText);
        Assert.Equal((16L, 75), (viewer.ElementAt(75), viewer.DisplayOffsetOf(16)));
        viewer.Hex = false;
        viewer.Hex = true;
        await viewer.HexPending;
        Assert.Single(source.MemoryReads); // the bytes are read once
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-324")]
    public async Task ClosingCancelsReading()
    {
        // Close while a chunk is being read: the read's token is cancelled, nothing more is read, the loading ends quietly.
        var (debug, source) = Fake();
        source.Details = _ => Task.FromResult(Unicode(1_000_000));
        var tokens = new List<CancellationToken>();
        source.Read = (start, count, ct) =>
        {
            tokens.Add(ct);
            if (start > 0)
            {
                var pending = new TaskCompletionSource<TextChunk>();
                ct.Register(() => pending.TrySetCanceled(ct));
                return pending.Task;
            }

            return Task.FromResult(new TextChunk(0, count, new string('x', count), false, false));
        };
        var viewer = Open(debug);
        Assert.Equal(2, source.Reads.Count);
        Assert.False(tokens[1].IsCancellationRequested);
        await viewer.CloseButton.PressAsync();
        Assert.True(viewer.IsClosed);
        Assert.True(tokens[1].IsCancellationRequested);
        await viewer.Loading; // ends without a fault
        Assert.Equal(2, source.Reads.Count);
        Assert.Equal(65536, viewer.Text.Length);

        // Closed (Escape) while the details are asked for: no text is read.
        (debug, source) = Fake();
        var details = new TaskCompletionSource<TextDetails>();
        CancellationToken detailsToken = default;
        source.Details = ct =>
        {
            detailsToken = ct;
            ct.Register(() => details.TrySetCanceled(ct));
            return details.Task;
        };
        viewer = Open(debug);
        await viewer.PressCancelAsync();
        Assert.True(detailsToken.IsCancellationRequested);
        await viewer.Loading;
        Assert.Empty(source.Reads);
    }
}
