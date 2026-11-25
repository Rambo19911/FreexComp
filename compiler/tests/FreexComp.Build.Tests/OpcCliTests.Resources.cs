using FreexComp.Compiler;
using FreexComp.Compiler.Toolchain;
using FreexComp.Opc;
using FreexComp.Prc;
using FreexComp.Tests.Shared;

namespace FreexComp.Build.Tests;

/// <summary>resource-compiler.md §6 (C-333): the <c>{$R}</c> directive with <c>opc</c> alone and the pinned tools.</summary>
public sealed partial class OpcCliTests
{
    /// <summary>
    /// CMP-1324: a compiled resource file named by the directive is linked as it is, a script is compiled first (with the <c>-D</c>
    /// values of the command line, CMP-1314); both are in the image. A second run starts no resource tool; a missing file is E0002
    /// at the directive.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1323")]
    [Trait("Req", "CMP-1324")]
    public async Task ResourceDirective_LinksResAndCompilesRc()
    {
        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "res"));
        var ct = TestContext.Current.CancellationToken;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Hello.pas"), "program Hello;\n{$R res\\data.rc}\n{$R *}\nbegin\n  Writeln('hi')\nend.\n");
            File.WriteAllText(Path.Combine(dir, "res", "data.rc"), "#ifdef WIDE\nBIG RCDATA BEGIN 1, 2, 3 END\n#else\nSMALL RCDATA BEGIN 1 END\n#endif\n");

            // Hello.res: made by the resource compiler tool from a script of its own
            File.WriteAllText(Path.Combine(dir, "made.rc"), "9 RCDATA BEGIN 7 END\n");
            var prcOut = new StringWriter { NewLine = "\n" };
            var prc = PrcCommand.Run(
                ["made.rc", "-o", "Hello.res", "--toolchain", bin],
                new PrcEnvironment(dir, prcOut, new StringWriter(), PhysicalFileSystem.Instance), ct);
            Assert.True(prc == 0, prcOut.ToString());

            (int Code, string[] Lines) Opc(params string[] extra)
            {
                var stdout = new StringWriter { NewLine = "\n" };
                var stderr = new StringWriter();
                var code = OpcCommand.Run(
                    ["Hello.pas", "--rtl", Path.Combine(RepoPaths.Root, "rtl", "src"), "--toolchain", bin, "--verbose", .. extra],
                    new OpcEnvironment(dir, stdout, stderr, PhysicalFileSystem.Instance), ct);
                return (code, (stdout + stderr.ToString()).Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }

            static bool IsResourceTool(string line) => line.StartsWith("> ", StringComparison.Ordinal) && line.Contains("llvm-rc", StringComparison.OrdinalIgnoreCase);

            var (code, lines) = Opc("-D", "WIDE");

            Assert.True(code == 0, string.Join("\n", lines));
            Assert.Single(lines, IsResourceTool);
            Assert.True(File.Exists(Path.Combine(dir, "obj", "Debug", "data.res")));        // CMP-1316: the script's output is in the object directory
            Assert.False(File.Exists(Path.Combine(dir, "res", "data.res")));
            var exe = Path.Combine(dir, "Hello.exe");
            var resources = await ResourceNamesOfAsync(exe);
            Assert.Equal(["RCDATA (ID 10): BIG", "RCDATA (ID 10): (ID 9)", "MANIFEST (ID 24): (ID 1)"], resources);
            var output = new List<string>();
            Assert.Equal(0, await NativeRun.RunAsync(exe, dir, output.Add, _ => { }));
            Assert.Equal(["hi"], output);

            // nothing changed: no resource tool, no link
            (code, lines) = Opc("-D", "WIDE");
            Assert.True(code == 0, string.Join("\n", lines));
            Assert.DoesNotContain(lines, l => l.StartsWith("> ", StringComparison.Ordinal));

            // another define: the script is compiled again and the image has the other entry
            (code, lines) = Opc();
            Assert.True(code == 0, string.Join("\n", lines));
            Assert.Single(lines, IsResourceTool);
            Assert.Equal(["RCDATA (ID 10): SMALL", "RCDATA (ID 10): (ID 9)", "MANIFEST (ID 24): (ID 1)"], await ResourceNamesOfAsync(exe));

            // a missing file: E0002 at the directive, exit code 1
            File.Delete(Path.Combine(dir, "Hello.res"));
            (code, lines) = Opc();
            Assert.Equal(1, code);
            Assert.Contains(lines, l => l.EndsWith($"Hello.pas(3,1): error E0002: File not found: '{Path.Combine(dir, "Hello.res")}'", StringComparison.Ordinal));
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }

    /// <summary>The resources of an image as <c>&lt;type&gt;: &lt;name&gt;</c>, in the order the object reader of the toolchain prints them.</summary>
    private static async Task<List<string>> ResourceNamesOfAsync(string image)
    {
        var reader = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin", "llvm-readobj.exe");
        Assert.True(File.Exists(reader), reader + " is missing");
        var lines = new List<string>();
        void Line(string line)
        {
            lock (lines)
            {
                lines.Add(line);
            }
        }

        var exit = await ProcessRunner.Instance.RunAsync(reader, ["--coff-resources", image], Path.GetDirectoryName(image)!, Line, Line, TestContext.Current.CancellationToken);
        Assert.True(exit == 0, string.Join("\n", lines));
        var names = new List<string>();
        var type = "";
        foreach (var line in lines.Select(l => l.Trim()))
        {
            if (line.StartsWith("Type: ", StringComparison.Ordinal))
            {
                type = line["Type: ".Length..].TrimEnd('[').Trim();
            }
            else if (line.StartsWith("Name: ", StringComparison.Ordinal))
            {
                names.Add(type + ": " + line["Name: ".Length..].TrimEnd('[').Trim());
            }
        }

        return names;
    }
}
