using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace FreexComp.Build.Tests;

/// <summary>
/// C-329: the real command runner (build-events.md CMP-1271, 1272). Real processes — only the system's command interpreter
/// with harmless commands (<c>echo</c>, <c>exit</c>, <c>set /p</c>, <c>mkdir</c>, redirection into the test's own temporary
/// directory); no network; every process a test starts has ended when the test returns.
/// </summary>
[Trait("Phase", "8")]
public sealed class ShellCommandRunnerTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);
    private static readonly string Interpreter = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));

    public ShellCommandRunnerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => TempDirectories.Delete(_dir);

    private async Task<(int ExitCode, List<string> Out, List<string> Err)> RunAsync(string command, ShellCommandRunner? runner = null, string? directory = null)
    {
        var stdout = new List<string>();
        var stderr = new List<string>();
        var exitCode = await (runner ?? ShellCommandRunner.Instance)
            .RunAsync(command, directory ?? _dir, Add(stdout), Add(stderr), TestContext.Current.CancellationToken)
            .WaitAsync(Limit, TestContext.Current.CancellationToken);
        return (exitCode, stdout, stderr);

        static Action<string> Add(List<string> lines) => line =>
        {
            lock (lines)
            {
                lines.Add(line);
            }
        };
    }

    private static bool HasExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;                                                         // no such process any more
        }
    }

    [Fact]
    [Trait("Req", "CMP-1271")]
    public async Task RunsThroughTheSystemCommandInterpreter()
    {
        // echo, redirection into a file, a quoted path with a blank, &&
        var (code, stdout, stderr) = await RunAsync("echo hello");
        Assert.Equal((0, "hello"), (code, Assert.Single(stdout)));
        Assert.Empty(stderr);

        Directory.CreateDirectory(Path.Combine(_dir, "sub dir"));
        Assert.Equal(0, (await RunAsync("echo one> out.txt")).ExitCode);
        Assert.Equal("one\r\n", File.ReadAllText(Path.Combine(_dir, "out.txt")));
        Assert.Equal(0, (await RunAsync("""echo two> "sub dir\file name.txt" """.Trim())).ExitCode);
        Assert.Equal("two\r\n", File.ReadAllText(Path.Combine(_dir, "sub dir", "file name.txt")));
        Assert.Equal(["a", "b"], (await RunAsync("echo a&& echo b")).Out);
        Assert.Equal(["first"], (await RunAsync("echo first& exit /b 1 && echo never")).Out);
        // quotes inside a command work as typed in a command prompt
        Assert.Equal(["\"a b\" \"c\""], (await RunAsync("echo \"a b\" \"c\"")).Out);
        Assert.Equal(["quoted"], (await RunAsync($"\"{Interpreter}\" /d /c \"echo quoted\"")).Out);

        // the interpreter is the one of the system directory — also when the working directory has a file of that name and the
        // child's environment points elsewhere (changed for the child only: the test's own environment is not touched)
        var decoy = Path.Combine(_dir, "cmd.exe");
        File.WriteAllText(decoy, "not a program");
        File.WriteAllText(Path.Combine(_dir, "chcp.com"), "not a program");
        ProcessStartInfo? start = null;
        var pid = 0;
        var runner = new ShellCommandRunner
        {
            Starting = s =>
            {
                start = s;
                s.Environment["ComSpec"] = decoy;
                s.Environment["PATH"] = _dir + ";" + s.Environment["PATH"];
            },
            ProcessStarted = id => pid = id,
        };

        // the command waits for a line from a pipe of this test, so the started process can be looked at while it runs
        var pipeName = "freexcomp-test-" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var command = $"set /p X=<\\\\.\\pipe\\{pipeName}& echo %ComSpec%";
        var run = RunAsync(command, runner);
        string? image;
        try
        {
            await pipe.WaitForConnectionAsync(TestContext.Current.CancellationToken).WaitAsync(Limit, TestContext.Current.CancellationToken);
            using var process = Process.GetProcessById(pid);
            image = process.MainModule?.FileName;
        }
        finally
        {
            if (pipe.IsConnected)
            {
                await pipe.WriteAsync(Encoding.ASCII.GetBytes("go\r\n"), TestContext.Current.CancellationToken);     // lets the command go on
                await pipe.FlushAsync(TestContext.Current.CancellationToken);
            }
        }

        var (decoyCode, decoyOut, _) = await run;

        Assert.Equal(0, decoyCode);
        Assert.Equal([decoy], decoyOut);                                         // the environment did point elsewhere
        Assert.NotNull(start);
        Assert.Equal(Interpreter, start.FileName);
        Assert.True(Path.IsPathFullyQualified(start.FileName));
        Assert.Equal(Interpreter, image, ignoreCase: true);
        // the fixed outer command line: both interpreters and the code-page program by full path; the command text is not in it
        var system = Environment.SystemDirectory;
        Assert.Equal($"/d /v:on /s /c \"\"{system}\\chcp.com\" 65001>nul&\"{system}\\cmd.exe\" /d /v:off /s /c \"!FREEXCOMP_EVENT_COMMAND!\"\"", start.Arguments);
        Assert.True(Path.IsPathFullyQualified(system));
        Assert.DoesNotContain("set /p", start.Arguments, StringComparison.Ordinal);
        Assert.Equal(command, start.Environment["FREEXCOMP_EVENT_COMMAND"]);
        Assert.Empty(start.ArgumentList);                                        // one raw string, not an argument list
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Equal(_dir, start.WorkingDirectory);
    }

    [Fact]
    [Trait("Req", "CMP-1271")]
    public async Task ExitCodeIsReturned()
    {
        Assert.Equal(3, (await RunAsync("exit /b 3")).ExitCode);
        Assert.Equal(5, (await RunAsync("exit 5")).ExitCode);
        Assert.Equal(0, (await RunAsync("exit /b 0")).ExitCode);
        Assert.Equal(7, (await RunAsync("echo before& exit /b 7")).ExitCode);
        Assert.Equal(-2, (await RunAsync("exit /b -2")).ExitCode);

        // a failing external program: its exit code
        Assert.Equal(9, (await RunAsync($"\"{Interpreter}\" /d /c exit 9")).ExitCode);
        Assert.Equal(1, (await RunAsync($"\"{Path.Combine(Environment.SystemDirectory, "findstr.exe")}\" freexcomp-no-match nul")).ExitCode);
        Assert.Equal(2, (await RunAsync("echo a& exit /b 2& echo never")).ExitCode);

        // a program that does not exist: the interpreter's non-zero code and its message on standard error
        var (code, stdout, stderr) = await RunAsync("freexcomp-no-such-program-0f3a arg");
        Assert.NotEqual(0, code);
        Assert.Empty(stdout);
        Assert.Contains(stderr, line => line.Contains("freexcomp-no-such-program-0f3a", StringComparison.Ordinal));
    }

    /// <summary>
    /// The command text reaches the second interpreter exactly as stored: the outer level never parses it (it only inserts the
    /// value of one environment variable, which is not scanned again), and the second level reads it as a command prompt would.
    /// </summary>
    [Theory]
    [Trait("Req", "CMP-1271")]
    [InlineData("echo a& echo b", "a\nb")]
    [InlineData("echo a&& echo b|| echo c", "a\nb")]
    [InlineData("echo one two| \"%SystemRoot%\\System32\\findstr.exe\" two", "one two")]
    [InlineData("echo \"a > b | c & d\"", "\"a > b | c & d\"")]
    [InlineData("echo 100%& echo %OS%& echo %FreexCompNoSuchVariable%", "100%\nWindows_NT\n%FreexCompNoSuchVariable%")]
    [InlineData("echo a^&b ^^ ^| ^> ^<", "a&b ^ | > <")]
    [InlineData("echo wow! !x! !OS! \"!OS!\"", "wow! !x! !OS! \"!OS!\"")]
    [InlineData("echo \"a b\" \"c\" \"", "\"a b\" \"c\" \"")]
    [InlineData("echo \"half open", "\"half open")]
    [InlineData("for %i in (1 2) do @echo item %i", "item 1\nitem 2")]
    [InlineData("for /L %i in (1,1,2) do @(echo %i^&x)", "1&x\n2&x")]
    [InlineData("echo !FREEXCOMP_EVENT_COMMAND!", "!FREEXCOMP_EVENT_COMMAND!")]
    [InlineData("echo \"!FREEXCOMP_EVENT_COMMAND!\"& echo ^!FREEXCOMP_EVENT_COMMAND^!", "\"!FREEXCOMP_EVENT_COMMAND!\"\n!FREEXCOMP_EVENT_COMMAND!")]
    [InlineData("echo (a) ^(b^)& (echo c)", "(a) (b)\nc")]
    [InlineData("  echo spaced  ", "spaced  ")]
    public async Task CommandTextIsNeverParsedByTheOuterLevel(string command, string expected)
    {
        var (code, stdout, stderr) = await RunAsync(command);

        Assert.Equal(expected.Split('\n'), stdout);
        Assert.Empty(stderr);
        Assert.Equal(0, code);
    }

    [Fact]
    [Trait("Req", "CMP-1271")]
    public async Task RedirectionInTheCommandBelongsToTheCommand()
    {
        // > and >> of the command write the command's output, not the outer level's
        Assert.Equal((0, 0), await Counts("echo first> r.txt& echo second>> r.txt& (echo third)>> \"r 2.txt\""));
        Assert.Equal("first\r\nsecond\r\n", File.ReadAllText(Path.Combine(_dir, "r.txt")));
        Assert.Equal("third\r\n", File.ReadAllText(Path.Combine(_dir, "r 2.txt")));
        Assert.Equal(["first", "second"], (await RunAsync("type r.txt")).Out);

        async Task<(int, int)> Counts(string command)
        {
            var (code, stdout, _) = await RunAsync(command);
            return (code, stdout.Count);
        }
    }

    /// <summary>CMP-1263: the one variable the runner adds is set for the child only; a variable of that name in the parent is overwritten there and stays as it was here.</summary>
    [Fact]
    [Trait("Req", "CMP-1263")]
    public async Task CommandVariableIsSetForTheChildOnly()
    {
        const string Name = "FREEXCOMP_EVENT_COMMAND";
        var before = Environment.GetEnvironmentVariable(Name);
        Environment.SetEnvironmentVariable(Name, "echo from the parent");
        try
        {
            // the command prints the variable as the child sees it (an ordinary reference, expanded once by the second interpreter)
            var (code, stdout, _) = await RunAsync("echo %FREEXCOMP_EVENT_COMMAND%");

            Assert.Equal(0, code);
            // the child ran the given command, and its variable holds that command, not the parent's value
            Assert.Equal(["echo %FREEXCOMP_EVENT_COMMAND%"], stdout);
            Assert.Equal("echo from the parent", Environment.GetEnvironmentVariable(Name));       // the parent's environment is untouched
        }
        finally
        {
            Environment.SetEnvironmentVariable(Name, before);
        }

        // nothing else is added or removed
        IDictionary<string, string?>? child = null;
        var runner = new ShellCommandRunner { Starting = s => child = new Dictionary<string, string?>(s.Environment, StringComparer.OrdinalIgnoreCase) };
        Assert.Equal(0, (await RunAsync("exit /b 0", runner)).ExitCode);
        var parent = Environment.GetEnvironmentVariables();
        Assert.NotNull(child);
        Assert.Equal(
            parent.Keys.Cast<string>().Where(k => !k.Equals(Name, StringComparison.OrdinalIgnoreCase)).Append(Name).Order(StringComparer.OrdinalIgnoreCase),
            child.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("exit /b 0", child[Name]);
        Assert.All(child.Where(p => !p.Key.Equals(Name, StringComparison.OrdinalIgnoreCase)), p => Assert.Equal((string?)parent[p.Key], p.Value));
    }

    [Fact]
    [Trait("Req", "CMP-1263")]
    public async Task WorkingDirectoryIsUsed()
    {
        var sub = Path.Combine(_dir, "work here");
        Directory.CreateDirectory(sub);

        var (code, stdout, _) = await RunAsync("cd& echo x> relative.txt", directory: sub);

        Assert.Equal(0, code);
        Assert.Equal([sub], stdout);
        Assert.True(File.Exists(Path.Combine(sub, "relative.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "relative.txt")));
    }

    [Fact]
    [Trait("Req", "CMP-1271")]
    public async Task NonAsciiTextRoundTrips()
    {
        const string Text = "š ж 日 – žluťoučký кот 日本語";

        var (code, stdout, stderr) = await RunAsync($"echo {Text}& (echo {Text})1>&2");

        Assert.Equal(0, code);
        Assert.Equal([Text], stdout);
        Assert.Equal([Text], stderr);

        // a directory and a file with such names are created by commands; the file's bytes are UTF-8
        Assert.Equal(0, (await RunAsync("mkdir \"dir š ж 日\"& echo ž> \"dir š ж 日\\файл š.txt\"")).ExitCode);
        Assert.True(Directory.Exists(Path.Combine(_dir, "dir š ж 日")));
        Assert.Equal(Encoding.UTF8.GetBytes("ž\r\n"), File.ReadAllBytes(Path.Combine(_dir, "dir š ж 日", "файл š.txt")));
        Assert.Equal(["dir š ж 日"], (await RunAsync("dir /b /ad")).Out);

        // the switch of the code page produces no output of its own and leaves the exit code to the command
        var (quietCode, quietOut, quietErr) = await RunAsync("exit /b 4");
        Assert.Equal((4, 0, 0), (quietCode, quietOut.Count, quietErr.Count));
    }

    [Fact]
    [Trait("Req", "CMP-1271")]
    public async Task StandardInputIsClosed()
    {
        // a command that reads input gets end-of-file instead of waiting
        var (_, stdout, _) = await RunAsync("set /p X=& echo after");
        Assert.Equal(["after"], stdout);

        var (code, _, _) = await RunAsync("pause>nul& exit /b 6");
        Assert.Equal(6, code);
    }

    [Fact]
    [Trait("Req", "CMP-1265")]
    public async Task StdErrIsCaptured()
    {
        var (code, stdout, stderr) = await RunAsync("echo out 1& (echo err 1)1>&2& echo out 2& (echo err 2)1>&2");

        Assert.Equal(0, code);
        Assert.Equal(["out 1", "out 2"], stdout);                                // the order of the lines of one stream is kept
        Assert.Equal(["err 1", "err 2"], stderr);

        // far more than a pipe buffers, on both streams: every line arrives before the exit code
        var (_, manyOut, manyErr) = await RunAsync("for /L %i in (1,1,2000) do @(echo out %i& (echo err %i)1>&2)");
        Assert.Equal(Enumerable.Range(1, 2000).Select(i => "out " + i), manyOut);
        Assert.Equal(Enumerable.Range(1, 2000).Select(i => "err " + i), manyErr);
    }

    [Fact]
    [Trait("Req", "CMP-1269")]
    public async Task CancellationKillsTheProcessTree()
    {
        // the command starts a second interpreter that waits for a line from a pipe only this test can write to
        var pipeName = "freexcomp-test-" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pid = 0;
        var runner = new ShellCommandRunner { ProcessStarted = id => pid = id };
        var lines = new List<string>();

        var run = runner.RunAsync($"echo started& \"{Interpreter}\" /d /c \"set /p X=<\\\\.\\pipe\\{pipeName}\"& echo never", _dir,
            line => { lock (lines) { lines.Add(line); } }, _ => { }, cts.Token);
        await pipe.WaitForConnectionAsync(TestContext.Current.CancellationToken).WaitAsync(Limit, TestContext.Current.CancellationToken);   // the child runs and waits
        Assert.False(run.IsCompleted);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Limit, TestContext.Current.CancellationToken));
        Assert.NotEqual(0, pid);
        Assert.True(HasExited(pid), "the cancelled interpreter is still running");
        // the child held the only client end of the pipe: the pipe ends exactly when that process is gone
        Assert.Equal(0, await ReadToEndOfPipeAsync(pipe));
        lock (lines)
        {
            Assert.Equal(["started"], lines);                                    // nothing after the kill
        }
    }

    [Fact]
    [Trait("Req", "CMP-1272")]
    public async Task DetachedChildDoesNotBlockCompletion()
    {
        var pipeName = "freexcomp-test-" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var late = new List<string>();
        var returned = false;
        void Line(string line)
        {
            lock (late)
            {
                if (returned)
                {
                    late.Add(line);
                }
            }
        }

        // `start /b` leaves a child behind that holds the output pipes and waits for a line from this test's pipe; then it prints
        var clock = Stopwatch.StartNew();
        var exitCode = await ShellCommandRunner.Instance
            .RunAsync($"start \"\" /b \"{Interpreter}\" /d /c \"set /p X=<\\\\.\\pipe\\{pipeName}& echo late\"& exit /b 4", _dir, Line, Line, TestContext.Current.CancellationToken)
            .WaitAsync(Limit, TestContext.Current.CancellationToken);
        var elapsed = clock.Elapsed;
        lock (late)
        {
            returned = true;
        }

        try
        {
            Assert.Equal(4, exitCode);                                           // the interpreter's own exit code
            Assert.True(elapsed < TimeSpan.FromSeconds(5), $"the runner waited {elapsed} for a detached child");
            await pipe.WaitForConnectionAsync(TestContext.Current.CancellationToken).WaitAsync(Limit, TestContext.Current.CancellationToken);
            Assert.True(pipe.IsConnected);                                       // the child was left running
        }
        finally
        {
            // the test ends the child itself: the line it waits for (or, had it not connected, the end of the pipe)
            if (pipe.IsConnected)
            {
                await pipe.WriteAsync(Encoding.ASCII.GetBytes("go\r\n"), TestContext.Current.CancellationToken);
                await pipe.FlushAsync(TestContext.Current.CancellationToken);
            }
        }

        Assert.Equal(0, await ReadToEndOfPipeAsync(pipe));                       // the child has exited
        lock (late)
        {
            Assert.Empty(late);                                                  // what a detached child prints later is not delivered
        }
    }

    /// <summary>Reads until the client end is closed — the process that held it has exited; 0 = the end was reached.</summary>
    private static async Task<int> ReadToEndOfPipeAsync(NamedPipeServerStream pipe)
    {
        var buffer = new byte[256];
        try
        {
            while (true)
            {
                var read = await pipe.ReadAsync(buffer, TestContext.Current.CancellationToken).AsTask().WaitAsync(Limit, TestContext.Current.CancellationToken);
                if (read == 0)
                {
                    return 0;
                }
            }
        }
        catch (IOException)
        {
            return 0;                                                            // the pipe broke: the client is gone
        }
    }
}
