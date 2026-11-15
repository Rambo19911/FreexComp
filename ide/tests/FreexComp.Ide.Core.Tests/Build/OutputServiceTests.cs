using System.Globalization;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

[Trait("Phase", "3")]
public sealed class OutputServiceTests
{
    private static readonly TimeSpan Batch = TimeSpan.FromMilliseconds(50);
    private readonly ManualClock _clock = new();

    /// <summary>IDE-P3-070, UI-TW-505: separate channels, batched updates, line limit with a single truncation notice.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-070")]
    [Trait("Req", "UI-TW-505")]
    public void ChannelsLimitsAndTruncationNotice()
    {
        var services = TestComposition.Build(o => o.Clock = _clock);
        services.Settings.Set(SettingsCatalog.OutputMaxLines, 1000);
        var output = new OutputService(_clock, services.Settings);
        var changes = new List<string>();
        output.Changed += (_, channel) => changes.Add(channel);

        Assert.Equal(["build", "program"], output.Channels);
        output.Append(OutputChannels.Build, "Building Hello (Debug, win64)", OutputKind.Ide);
        output.Append(OutputChannels.Program, "Hello", OutputKind.Normal);
        output.Append(OutputChannels.Program, "oops", OutputKind.Error);
        Assert.Empty(output.GetLines(OutputChannels.Build)); // batched: nothing before the 50 ms flush
        Assert.Empty(changes);

        _clock.Advance(Batch);
        Assert.Equal([new OutputLine("Building Hello (Debug, win64)", OutputKind.Ide)], output.GetLines(OutputChannels.Build));
        Assert.Equal([new OutputLine("Hello", OutputKind.Normal), new OutputLine("oops", OutputKind.Error)], output.GetLines(OutputChannels.Program));
        Assert.Equal(["build", "program"], changes.Order());

        // One Append may carry several lines; every line ending counts.
        output.Append(OutputChannels.Build, "a\r\nb\nc\rd", OutputKind.Normal);
        _clock.Advance(Batch);
        Assert.Equal(["Building Hello (Debug, win64)", "a", "b", "c", "d"], output.GetLines(OutputChannels.Build).Select(l => l.Text));

        // A burst over the limit: the oldest lines go in chunks of 10 %, the notice is the first line, once.
        changes.Clear();
        for (var i = 0; i < 1200; i++)
            output.Append(OutputChannels.Program, "line " + i.ToString(CultureInfo.InvariantCulture), OutputKind.Normal);
        _clock.Advance(Batch);
        var lines = output.GetLines(OutputChannels.Program);
        var notice = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Output_Truncated, 1000);
        Assert.Equal(new OutputLine(notice, OutputKind.Ide), lines[0]);
        Assert.Equal(1, lines.Count(l => l.Text == notice));
        Assert.InRange(lines.Count - 1, 900, 1000);
        Assert.Equal("line 1199", lines[^1].Text);
        Assert.Equal(["program"], changes); // one update for the whole burst
        output.Append(OutputChannels.Program, "more", OutputKind.Normal);
        _clock.Advance(Batch);
        Assert.Equal(1, output.GetLines(OutputChannels.Program).Count(l => l.Text == notice));

        // Clear empties one channel only and forgets its notice.
        output.Clear(OutputChannels.Program);
        Assert.Empty(output.GetLines(OutputChannels.Program));
        Assert.NotEmpty(output.GetLines(OutputChannels.Build));
        Assert.Throws<ArgumentException>(() => output.Append("nope", "x", OutputKind.Normal));
    }

    /// <summary>IDE-P3-073: lines split on CRLF / LF / CR across chunks, a partial line shows after 100 ms, ANSI removed.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-073")]
    public void PartialLineFlushAndAnsiStripping()
    {
        var lines = new List<string>();
        var splitter = new OutputLineSplitter(_clock, lines.Add);

        splitter.Write("one\r");
        Assert.Empty(lines); // a CR at the end of a chunk may start a CRLF
        splitter.Write("\ntwo\nthr");
        Assert.Equal(["one", "two"], lines);
        splitter.Write("ee\r\x1B[31mred\x1B[0m\r");
        Assert.Equal(["one", "two", "three"], lines);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(["one", "two", "three", "red"], lines); // the held CR ends the line once no CRLF can follow

        splitter.Write("Name: ");
        _clock.Advance(TimeSpan.FromMilliseconds(99));
        Assert.Equal(4, lines.Count);
        splitter.Write("\x1B[1;32m"); // more data restarts the 100 ms
        _clock.Advance(TimeSpan.FromMilliseconds(99));
        Assert.Equal(4, lines.Count);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("Name: ", lines[^1]); // the prompt without a line break appears

        splitter.Write("last");
        splitter.Complete(); // the pipe ended: the rest is flushed at once
        Assert.Equal("last", lines[^1]);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(6, lines.Count);

        Assert.Equal("plain text", OutputLineSplitter.StripAnsi("\x1B[0;1mplain\x1B[K text\x1B[?25h"));
    }
}
