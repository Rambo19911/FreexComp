using System.Text;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;
using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>
/// resource-compiler.md CMP-1305, 1306: what the pinned resource tool really does — the facts the resource compilation step
/// relies on, proven on the real tool in an own temporary directory.
/// </summary>
[Trait("Phase", "8")]
[Trait("Category", "Toolchain")]
public sealed class ResourceToolFactsTests
{
    [Fact]
    [Trait("Req", "CMP-1305")]
    [Trait("Req", "CMP-1306")]
    public async Task PinnedToolBehaviour()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var tool = new ToolchainPaths(Path.GetDirectoryName(Clang.Path)!).ResourceTool;
        Assert.True(File.Exists(tool), $"Pinned toolchain missing: {tool} (run scripts/bootstrap.ps1).");
        var dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        var ct = TestContext.Current.CancellationToken;
        try
        {
            string scripts = Path.Combine(dir, "scripts"), one = Path.Combine(dir, "inc one"), two = Path.Combine(dir, "inc2"), obj = Path.Combine(dir, "obj");
            void Write(string path, string text) => PhysicalFileSystem.Instance.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));

            // the data files differ in length, so the length of an entry tells which file was taken
            Write(Path.Combine(scripts, "a.bin"), "1");                     // beside the script: wins
            Write(Path.Combine(one, "a.bin"), "22");
            Write(Path.Combine(obj, "a.bin"), "55555");                     // beside the tool input: must not be taken
            Write(Path.Combine(one, "b.bin"), "22");                        // first include directory: wins over the second
            Write(Path.Combine(two, "b.bin"), "333");
            Write(Path.Combine(two, "c.bin"), "333");
            Write(Path.Combine(scripts, "facts.rc"), "the tool reads the preprocessed input, not this file");
            var request = new ResourceCompileRequest(Path.Combine(scripts, "facts.rc"), Path.Combine(obj, "facts.res"), obj) { IncludeDirectories = [one, two] };

            async Task<(int ExitCode, List<string> Output)> RunTool(string input)
            {
                Write(ResourceCompiler.ToolInputPath(request), input);
                var output = new List<string>();
                void Line(string line)
                {
                    lock (output)
                    {
                        output.Add(line);
                    }
                }

                // exactly the command line of CMP-1305, run where ResourceCompiler runs it: in the script's directory
                var exit = await ProcessRunner.Instance.RunAsync(tool, ResourceCompiler.CompileArguments(request), scripts, Line, Line, ct);
                return (exit, output);
            }

            // facts 1-3: the options are accepted; data files are found beside the script first, then in the include directories
            // in order, although the input lies in another directory; the input is read as UTF-8
            var (ok, okOutput) = await RunTool("1 RCDATA \"a.bin\"\n2 RCDATA \"b.bin\"\n3 RCDATA \"c.bin\"\nSTRINGTABLE\nBEGIN\n  1 \"żółw\"\nEND\n");

            Assert.True(ok == 0, string.Join("\n", okOutput));
            var compiled = File.ReadAllBytes(request.OutputPath + ".tmp");
            Assert.Equal(
                [new ResourceEntry(10, 1, 0x0409, 1), new ResourceEntry(10, 2, 0x0409, 2), new ResourceEntry(10, 3, 0x0409, 3), new ResourceEntry(6, 1, 0x0409, 40)],
                ResourceFile.Read(compiled));
            Assert.True(compiled.AsSpan().IndexOf(Encoding.Unicode.GetBytes("żółw")) > 0, "the string is not stored as the UTF-16 form of the UTF-8 input");

            // the three outputs of CMP-1306, exactly: the tool names no line of its input
            var (unknownExit, unknown) = await RunTool("1 RCDATA \"a.bin\"\nFROBNICATE 1, 2\n");
            Assert.Equal(1, unknownExit);
            Assert.Equal(["llvm-rc: Error parsing file: expected '{', got ,"], unknown);

            var (missingEndExit, missingEnd) = await RunTool("1 RCDATA \"a.bin\"\n\n2 RCDATA\nBEGIN\n  1, 2\n");
            Assert.Equal(1, missingEndExit);
            Assert.Equal(["llvm-rc: Error parsing file: expected int or string, got <EOF>"], missingEnd);

            var (missingFileExit, missingFile) = await RunTool("\n1 ICON \"nothere.ico\"\n");
            Assert.Equal(1, missingFileExit);
            Assert.Equal(["llvm-rc: Error in ICON statement (ID 1): ", "error : file not found : nothere.ico"], missingFile);
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }

    /// <summary>
    /// C-334: the pinned tool looks for a data file in the directories of the <c>INCLUDE</c> environment variable after its
    /// include directories (shown with the command line of CMP-1305 without <c>/X</c>). The compile step passes <c>/X</c>, so a
    /// file that is reachable only through the variable is not taken and the environment cannot change a build.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1305")]
    public async Task IncludeVariable_IsSearchedByTheTool_NotByTheCompileStep()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var tool = new ToolchainPaths(Path.GetDirectoryName(Clang.Path)!).ResourceTool;
        Assert.True(File.Exists(tool), $"Pinned toolchain missing: {tool} (run scripts/bootstrap.ps1).");
        var dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        var ct = TestContext.Current.CancellationToken;
        try
        {
            string scripts = Path.Combine(dir, "scripts"), inc = Path.Combine(dir, "inc"), fromVariable = Path.Combine(dir, "from variable"), obj = Path.Combine(dir, "obj");
            void Write(string path, string text) => PhysicalFileSystem.Instance.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
            Write(Path.Combine(scripts, "facts.rc"), "the tool reads the preprocessed input, not this file");
            Write(Path.Combine(inc, "listed.bin"), "22");
            Write(Path.Combine(fromVariable, "listed.bin"), "4444");                 // an include directory wins over the variable
            Write(Path.Combine(fromVariable, "only.bin"), "4444");                   // reachable only through the variable
            var request = new ResourceCompileRequest(Path.Combine(scripts, "facts.rc"), Path.Combine(obj, "facts.res"), obj) { IncludeDirectories = [inc] };
            Write(ResourceCompiler.ToolInputPath(request), "1 RCDATA \"listed.bin\"\n2 RCDATA \"only.bin\"\n");

            // the tool as the compile step starts it (script directory, arguments of CMP-1305), with the variable set for this process only
            async Task<(int ExitCode, string[] Output)> RunTool(IEnumerable<string> arguments)
            {
                File.Delete(request.OutputPath + ".tmp");
                var start = new System.Diagnostics.ProcessStartInfo(tool)
                {
                    WorkingDirectory = scripts,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var argument in arguments)
                {
                    start.ArgumentList.Add(argument);
                }

                start.Environment["INCLUDE"] = fromVariable;
                using var process = System.Diagnostics.Process.Start(start)!;
                try
                {
                    var stdout = PipePump.Text(process.StandardOutput, "stdout");           // each pipe on its own thread
                    var stderr = PipePump.Text(process.StandardError, "stderr");
                    await process.WaitForExitAsync(ct);
                    return (process.ExitCode, (await stdout + await stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }

            var arguments = ResourceCompiler.CompileArguments(request);
            Assert.Contains("/X", arguments);

            // the fact: without /X the file of the variable's directory is taken (4 bytes), after the include directories (2 bytes)
            var (searchedExit, searched) = await RunTool(arguments.Remove("/X"));
            Assert.True(searchedExit == 0, string.Join("\n", searched));
            Assert.Equal(
                [new ResourceEntry(10, 1, 0x0409, 2), new ResourceEntry(10, 2, 0x0409, 4)],
                ResourceFile.Read(File.ReadAllBytes(request.OutputPath + ".tmp")));

            // the compile step: the file reachable only through the variable is not taken
            var (exit, output) = await RunTool(arguments);
            Assert.Equal(1, exit);
            Assert.Equal(["llvm-rc: Error in 10 statement (ID 2):", "error : file not found : only.bin"], output);
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }

    /// <summary>
    /// C-335: the preprocessor of the toolchain adds the directories of the <c>CPATH</c> and <c>C_INCLUDE_PATH</c> environment
    /// variables to its search, even with <c>-nostdinc</c> (shown with the command line of CMP-1303). The compile step removes
    /// both variables from the preprocessor's environment, so a header that is reachable only through one of them is not
    /// found and the environment cannot change a build. The variable is set for the child process only.
    /// </summary>
    [Theory]
    [InlineData("CPATH")]
    [InlineData("C_INCLUDE_PATH")]
    [Trait("Req", "CMP-1303")]
    public async Task IncludePathVariable_IsSearchedByThePreprocessor_NotByTheCompileStep(string variable)
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var paths = new ToolchainPaths(Path.GetDirectoryName(Clang.Path)!);
        Assert.True(File.Exists(paths.ResourceTool), $"Pinned toolchain missing: {paths.ResourceTool} (run scripts/bootstrap.ps1).");
        var dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        var ct = TestContext.Current.CancellationToken;
        try
        {
            string scripts = Path.Combine(dir, "scripts"), fromVariable = Path.Combine(dir, "from variable"), obj = Path.Combine(dir, "obj");
            void Write(string path, string text) => PhysicalFileSystem.Instance.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
            var script = Path.Combine(scripts, "facts.rc");
            Write(script, "#include <only.h>\n1 RCDATA\nBEGIN\n  ONLY\nEND\n");
            Write(Path.Combine(fromVariable, "only.h"), "#define ONLY 7\n");       // the only copy: reachable only through the variable
            var request = new ResourceCompileRequest(script, Path.Combine(obj, "facts.res"), obj);
            ResourceHeaders.Write(PhysicalFileSystem.Instance, obj);

            // real processes; the variable exists in the environment of the started process only
            var processes = new ProcessRunner { Starting = start => start.Environment[variable] = fromVariable };

            // the fact: the preprocessor, started with the command line of CMP-1303 and its environment as it is, finds the header
            var lines = new List<string>();
            void Line(string line)
            {
                lock (lines)
                {
                    lines.Add(line);
                }
            }

            var exit = await processes.RunAsync(paths.Clang, ResourceCompiler.PreprocessArguments(request), obj, Line, Line, ct);
            Assert.True(exit == 0, string.Join("\n", lines));

            // the compile step: the header is not found
            var compiler = new ResourceCompiler(new ToolRunner(processes, new ToolRunOptions(Location.ForFile(script))), paths, PhysicalFileSystem.Instance);
            var result = await compiler.CompileAsync(request, ct);

            Assert.False(result.Success, "the header reachable only through " + variable + " was found");
            Assert.Contains(result.Diagnostics, d => d.Message.Contains("only.h", StringComparison.Ordinal));
            Assert.False(File.Exists(request.OutputPath));
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }
}
