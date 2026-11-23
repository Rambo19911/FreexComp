using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FreexComp.Debugger.Dap;

namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>
/// Starts the FakeAdapter helper exe with a scenario and talks raw DAP to it. The process (whole tree) is killed and
/// the scenario's temp directory deleted on dispose.
/// </summary>
internal sealed class FakeAdapterRunner : IAsyncDisposable
{
    /// <summary>Safety net only: every wait normally completes in milliseconds.</summary>
    public static readonly TimeSpan Safety = TimeSpan.FromSeconds(60);

    private readonly Process _process;
    private readonly DapTransport _transport;
    private readonly Task<string> _stderr;
    private readonly string _directory;
    private int _seq;

    private FakeAdapterRunner(Process process, string directory)
    {
        _process = process;
        _directory = directory;
        _transport = new DapTransport(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        _stderr = process.StandardError.ReadToEndAsync();
    }

    public static string ExePath => Path.Combine(AppContext.BaseDirectory, "FakeAdapter.exe");

    /// <summary>Writes the scenario into a fresh temp directory and returns the file path.</summary>
    public static string WriteScenario(string scenarioJson)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "scenario.json");
        File.WriteAllText(path, scenarioJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <param name="scenarioJson">Scenario text.</param>
    /// <param name="viaEnvironment">Pass the scenario path in the environment instead of as the argument.</param>
    public static FakeAdapterRunner Start(string scenarioJson, bool viaEnvironment = false)
    {
        var scenarioPath = WriteScenario(scenarioJson);
        var info = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        if (viaEnvironment)
            info.Environment["FAKEADAPTER_SCENARIO"] = scenarioPath;
        else
            info.ArgumentList.Add(scenarioPath);
        return new FakeAdapterRunner(Process.Start(info)!, Path.GetDirectoryName(scenarioPath)!);
    }

    /// <summary>Sends a request without waiting for anything.</summary>
    public async Task<int> SendAsync(string command, string? argumentsJson = null)
    {
        var seq = ++_seq;
        var json = argumentsJson is null
            ? $$"""{"seq":{{seq}},"type":"request","command":"{{command}}"}"""
            : $$"""{"seq":{{seq}},"type":"request","command":"{{command}}","arguments":{{argumentsJson}}}""";
        await _transport.SendAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None).WaitAsync(Safety);
        return seq;
    }

    /// <summary>The next message from the adapter; fails the test at end of stream.</summary>
    public async Task<JsonElement> ReadAsync()
    {
        var body = await _transport.ReadAsync(CancellationToken.None).WaitAsync(Safety);
        if (body is null)
            Assert.Fail("FakeAdapter closed its output; stderr: " + await StderrAsync());
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    /// <summary>Sends a request and returns the next message, which must be its response.</summary>
    public async Task<JsonElement> RequestAsync(string command, string? argumentsJson = null)
    {
        var seq = await SendAsync(command, argumentsJson);
        var message = await ReadAsync();
        Assert.Equal("response", message.GetProperty("type").GetString());
        Assert.Equal(seq, message.GetProperty("request_seq").GetInt32());
        Assert.Equal(command, message.GetProperty("command").GetString());
        return message;
    }

    /// <summary>Short description of the next message: <c>response:launch</c>, <c>event:stopped</c>.</summary>
    public async Task<string> ReadKindAsync()
    {
        var message = await ReadAsync();
        var type = message.GetProperty("type").GetString();
        return type + ":" + message.GetProperty(type == "event" ? "event" : "command").GetString();
    }

    public void CloseInput() => _process.StandardInput.Close();

    public async Task<int> WaitForExitAsync()
    {
        await _process.WaitForExitAsync().WaitAsync(Safety);
        return _process.ExitCode;
    }

    /// <summary>Everything the adapter wrote to stderr (complete once it has exited).</summary>
    public Task<string> StderrAsync() => _stderr.WaitAsync(Safety);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(Safety);
        }
        finally
        {
            _process.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
