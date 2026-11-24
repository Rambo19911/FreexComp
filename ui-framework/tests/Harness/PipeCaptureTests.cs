using System.Diagnostics;
using System.Text;

namespace FreexComp.UiFramework.Tests;

/// <summary>
/// U-142: the redirected stdout/stderr of a child process are read on dedicated background threads, never on the
/// thread pool (the pipe is a synchronous handle: an asynchronous read parks a pool thread until the child writes).
/// The child is a batch file that writes far more than a pipe buffer to both streams in turn, so a reader that
/// drained one stream after the other would never see it exit.
/// </summary>
public sealed class PipeCaptureTests
{
    private const int Lines = 4000;

    private static readonly string Pad = new('.', 80);

    private static string CommandProcessor => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static string Expected(string stream, string newLine)
    {
        var text = new StringBuilder();
        for (var i = 1; i <= Lines; i++)
        {
            text.Append(stream).Append("-line-").Append(i).Append(' ').Append(Pad).Append(newLine);
        }

        return text.ToString();
    }

    /// <summary>A batch file that writes <see cref="Lines"/> lines to stdout and to stderr, alternating.</summary>
    private static string NoisyScript()
    {
        var work = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "pipes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var script = Path.Combine(work, "noisy.cmd");
        File.WriteAllText(script,
            "@echo off\r\n"
            + $"for /L %%i in (1,1,{Lines}) do (\r\n"
            + $"  echo out-line-%%i {Pad}\r\n"
            + $"  1>&2 echo err-line-%%i {Pad}\r\n"
            + ")\r\n"
            + "exit /b 3\r\n");
        return script;
    }

    /// <summary>Asserts, for an ended capture, what its reading thread recorded about itself.</summary>
    private static void AssertDedicated(PipeCapture capture)
    {
        Assert.True(capture.Completion.IsCompletedSuccessfully);
        Assert.Equal((Pool: false, Background: true), capture.ReaderThread);
    }

    [Fact]
    public void PipeCapture_ReadsBothStreamsOnDedicatedThreads()
    {
        var psi = new ProcessStartInfo(CommandProcessor, $"/c \"{NoisyScript()}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        try
        {
            var stdout = PipeCapture.Start(process.StandardOutput, "test stdout");
            var stderr = PipeCapture.Start(process.StandardError, "test stderr");
            Assert.True(process.WaitForExit(Gui.SafetyNet), "the child did not exit: its pipes were not drained");
            Assert.Equal(3, process.ExitCode);
            Assert.Equal(Expected("out", "\r\n"), stdout.ReadToEnd());
            Assert.Equal(Expected("err", "\r\n"), stderr.ReadToEnd());
            Assert.Equal(stdout.ReadToEnd(), stdout.Text);
            AssertDedicated(stdout);
            AssertDedicated(stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact]
    public void PipeCapture_LineMode_EndsEveryLineWithLf()
    {
        var capture = PipeCapture.Start(new StringReader("one\r\ntwo\nthree"), "test lines", lines: true);
        Assert.Equal("one\ntwo\nthree\n", capture.ReadToEnd());
        AssertDedicated(capture);
    }

    [Fact]
    [Trait("Req", "UIF-1208")]
    public void GuiProcess_OutputReadOnDedicatedThreads()
    {
        using var app = Gui.StartGui(CommandProcessor, ["/c", NoisyScript()], new Dictionary<string, string>());
        Assert.True(app.WaitForExit(), app.ExitFailure("the noisy child"));
        AssertDedicated(app.OutputReader);
        AssertDedicated(app.ErrorReader);
        Assert.Equal(3, app.Process.ExitCode);
        Assert.Equal(Expected("out", "\n"), app.StdOut);
        Assert.Equal(Expected("err", "\n"), app.StdErr);
        Assert.True(app.WaitForOutputLine($"out-line-{Lines} {Pad}"));
    }

    [Fact]
    public async Task RunProcessAsync_LargeOutputOnBothStreams()
    {
        var script = NoisyScript();
        var result = await LayoutHost.RunProcessAsync(
            CommandProcessor, $"/c \"{script}\"", Path.GetDirectoryName(script)!, Gui.SafetyNet, TestContext.Current.CancellationToken);
        Assert.Null(result.Stopped);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(Expected("out", "\r\n"), result.StdOut);
        Assert.Equal(Expected("err", "\r\n"), result.StdErr);
    }

    /// <summary>No test source of the area reads a child's pipe asynchronously (each of these parks a pool thread).</summary>
    [Fact]
    public void Harness_NoAsynchronousPipeReads()
    {
        string[] banned =
        [
            "Begin" + "OutputReadLine", "Begin" + "ErrorReadLine", "OutputData" + "Received", "ErrorData" + "Received",
            "StandardOutput.ReadToEnd" + "Async", "StandardError.ReadToEnd" + "Async",
            "StandardOutput.ReadLine" + "Async", "StandardError.ReadLine" + "Async",
            "StandardOutput.Base" + "Stream", "StandardError.Base" + "Stream",
        ];
        var hits = Directory.EnumerateFiles(Path.Combine(UiCorpus.AreaDirectory, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => banned.Any(b => l.Text.Contains(b, StringComparison.Ordinal)))
            .Select(l => $"{l.File}({l.Line}): {l.Text.Trim()}")
            .ToList();
        Assert.True(hits.Count == 0, "asynchronous reads of a child's pipe (use PipeCapture):\n" + string.Join("\n", hits));
    }
}
