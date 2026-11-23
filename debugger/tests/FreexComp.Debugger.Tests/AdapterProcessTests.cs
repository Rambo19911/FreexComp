using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FreexComp.Debugger.Dap;
using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>Adapter process lifetime (architecture §3: DBG-004 … DBG-008).</summary>
[Trait("Category", "Integration")]
public sealed class AdapterProcessTests
{
    private static readonly TimeSpan Safety = FakeAdapterRunner.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "DBG-005")]
    [Trait("Req", "DBG-006")]
    public async Task Start_FakeAdapter_Echo()
    {
        // Non-ASCII text in both directions: stdio is UTF-8, and a byte-order mark on stdin would break the
        // adapter's header parsing (it exits with code 2 on a bad header).
        var scenario = FakeAdapterRunner.WriteScenario("""
            { "steps": [ { "on": "evaluate", "expectArguments": { "expression": "&Ēka€😀" }, "respond": { "result": "Žā€😀" } } ] }
            """);
        var stderrLines = new ConcurrentQueue<string>();
        try
        {
            await using var adapter = AdapterProcess.Start(FakeAdapterRunner.ExePath, [scenario], stderrLines.Enqueue);
            var transport = new DapTransport(adapter.StandardOutput, adapter.StandardInput);

            Assert.True(adapter.ProcessId > 0);
            Assert.False(adapter.HasExited);
            await transport.SendAsync(Encoding.UTF8.GetBytes(
                """{"seq":1,"type":"request","command":"evaluate","arguments":{"expression":"&Ēka€😀","frameId":1,"context":"watch"}}"""), Ct);
            var body = await transport.ReadAsync(Ct).WaitAsync(Safety, Ct);
            Assert.NotNull(body);
            using (var response = JsonDocument.Parse(body))
            {
                Assert.True(response.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("Žā€😀", response.RootElement.GetProperty("body").GetProperty("result").GetString());
            }

            adapter.CloseInput();
            await adapter.WaitForExitAsync(Ct).WaitAsync(Safety, Ct);
            await adapter.StderrCompleted.WaitAsync(Safety, Ct);

            Assert.True(adapter.HasExited);
            Assert.Equal(0, adapter.ExitCode);
            Assert.Null(await transport.ReadAsync(Ct).WaitAsync(Safety, Ct));
            Assert.Equal(["SCENARIO-COMPLETE"], stderrLines);
            Assert.Equal(["SCENARIO-COMPLETE"], adapter.LastStderrLines);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(scenario)!, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "DBG-005")]
    public async Task Start_WorkingDirectoryAndEnvironment()
    {
        var lines = new ConcurrentQueue<string>();
        await using var adapter = AdapterProcess.Start(
            FakeAdapterRunner.ExePath, ["--print-env", "LLDB_USE_NATIVE_PDB_READER"], lines.Enqueue);

        await adapter.WaitForExitAsync(Ct).WaitAsync(Safety, Ct);
        await adapter.StderrCompleted.WaitAsync(Safety, Ct);

        Assert.Equal(
            ["ENV LLDB_USE_NATIVE_PDB_READER=1", "CWD " + Path.GetDirectoryName(FakeAdapterRunner.ExePath)],
            lines);
    }

    [Fact]
    [Trait("Req", "DBG-006")]
    public async Task Stderr_KeepsLast20Lines()
    {
        await using var adapter = AdapterProcess.Start(FakeAdapterRunner.ExePath, ["--stderr-lines", "25"], null);

        await adapter.WaitForExitAsync(Ct).WaitAsync(Safety, Ct);
        await adapter.StderrCompleted.WaitAsync(Safety, Ct);

        Assert.Equal(Enumerable.Range(6, 20).Select(i => "line " + i), adapter.LastStderrLines);
    }

    [Fact]
    [Trait("Req", "DBG-004")]
    public void Missing_AdapterNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"), "lldb-dap.exe");

        var exception = Assert.Throws<DebuggerException>(() => AdapterProcess.Start(path, [], null));

        Assert.Equal(DebugErrorCode.AdapterNotFound, exception.Code);
        Assert.Equal("Debug adapter not found: " + path, exception.Message);
    }

    [Fact]
    [Trait("Req", "DBG-007")]
    public void NotExecutable_AdapterStartFailed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "not-an-adapter.exe");
            File.WriteAllText(path, "this is not an executable");

            var exception = Assert.Throws<DebuggerException>(() => AdapterProcess.Start(path, [], null));

            Assert.Equal(DebugErrorCode.AdapterStartFailed, exception.Code);
            Assert.StartsWith("Debug adapter could not be started: ", exception.Message);
            Assert.NotNull(exception.InnerException);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "DBG-008")]
    public async Task Dispose_KillsChildTree()
    {
        var childPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Process? child = null;
        var adapter = AdapterProcess.Start(FakeAdapterRunner.ExePath, ["--spawn-child"], line =>
        {
            if (line.StartsWith("CHILD-PID ", StringComparison.Ordinal))
                childPid.TrySetResult(int.Parse(line["CHILD-PID ".Length..], System.Globalization.CultureInfo.InvariantCulture));
        });
        try
        {
            child = Process.GetProcessById(await childPid.Task.WaitAsync(Safety, Ct));
            using var adapterProcess = Process.GetProcessById(adapter.ProcessId);
            Assert.False(child.HasExited);
            Assert.True(adapter.IsInJob);

            await adapter.DisposeAsync();

            // Both the adapter and the process it started are gone (the child never exits by itself).
            await adapterProcess.WaitForExitAsync(Ct).WaitAsync(Safety, Ct);
            await child.WaitForExitAsync(Ct).WaitAsync(Safety, Ct);
            Assert.True(adapter.HasExited);
            await adapter.DisposeAsync();   // idempotent
        }
        finally
        {
            await adapter.DisposeAsync();
            if (child is { HasExited: false })
                child.Kill(entireProcessTree: true);
            child?.Dispose();
        }
    }

    [Fact]
    public async Task Kill_EndsProcess_StreamsReportEof()
    {
        await using var adapter = AdapterProcess.Start(FakeAdapterRunner.ExePath, ["--spawn-child"], null);
        var transport = new DapTransport(adapter.StandardOutput, adapter.StandardInput);

        adapter.Kill();
        await adapter.WaitForExitAsync(Ct).WaitAsync(Safety, Ct);

        Assert.True(adapter.HasExited);
        Assert.NotEqual(0, adapter.ExitCode);
        Assert.Null(await transport.ReadAsync(Ct).WaitAsync(Safety, Ct));
        adapter.Kill();   // no-op on an exited process
    }
}
