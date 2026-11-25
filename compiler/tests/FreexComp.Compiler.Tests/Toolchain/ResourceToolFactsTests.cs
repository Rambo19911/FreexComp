using System.Text;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

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
}
