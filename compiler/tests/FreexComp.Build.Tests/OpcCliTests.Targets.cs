using System.Security.Cryptography;
using FreexComp.Compiler;
using FreexComp.Compiler.Targets;
using FreexComp.Opc;
using FreexComp.Tests.Shared;

namespace FreexComp.Build.Tests;

/// <summary>targets.md CMP-1017, CMP-1020 (task C-283): <c>opc --target &lt;id&gt;</c> and the target list of <c>opc --version</c>.</summary>
public sealed partial class OpcCliTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1017")]
    public async Task TargetOption_Win64EqualsDefault()
    {
        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            // the same program in two folders of the same name (paths are part of nothing that is compared)
            SortedDictionary<string, string> Build(string folder, params string[] extra)
            {
                var dir = Path.Combine(root, folder, "p");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "Hello.pas"), "program Hello; uses Greet; begin Writeln(Text) end.");
                File.WriteAllText(Path.Combine(dir, "Greet.pas"), "unit Greet; interface const Text = 'Hello, target!'; implementation end.");
                var stdout = new StringWriter { NewLine = "\n" };
                var stderr = new StringWriter();
                var code = OpcCommand.Run(
                    ["Hello.pas", "--rtl", Path.Combine(RepoPaths.Root, "rtl", "src"), "--toolchain", bin, .. extra],
                    new OpcEnvironment(dir, stdout, stderr, PhysicalFileSystem.Instance), TestContext.Current.CancellationToken);
                Assert.True(code == 0, stdout + stderr.ToString());
                Assert.Empty(stdout.ToString());

                // every file the run produced, relative to the folder; the hash of each .ll, .obj, import library and of the exe, and
                // of link.rsp with the folder's own path taken out (a .pcu records the paths and times of its sources: name only)
                var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(dir, file);
                    files[relative] = Path.GetExtension(file) switch
                    {
                        ".pas" or ".pcu" => "",
                        ".rsp" => File.ReadAllText(file).Replace(dir, "<dir>", StringComparison.OrdinalIgnoreCase),
                        _ => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))),
                    };
                }

                return files;
            }

            var plain = Build("a");
            var explicitTarget = Build("b", "--target", "win64");

            Assert.Contains(Path.Combine("obj", "Debug", "Hello.ll"), plain.Keys);
            Assert.Contains(Path.Combine("obj", "Debug", "System.ll"), plain.Keys);
            Assert.Contains("Hello.exe", plain.Keys);
            Assert.DoesNotContain(plain.Keys, k => k.Contains("win64", StringComparison.OrdinalIgnoreCase));      // CMP-1020: the baseline keeps obj\Debug
            Assert.Equal(plain.Keys, explicitTarget.Keys);
            var differing = plain.Keys.Where(k => plain[k] != explicitTarget[k]).ToList();
            Assert.True(differing.Count == 0, "--target win64 changes: " + string.Join(", ", differing));

            var output = new List<string>();
            var exit = await NativeRun.RunAsync(Path.Combine(root, "b", "p", "Hello.exe"), Path.Combine(root, "b", "p"), output.Add, _ => { });
            Assert.Equal(0, exit);
            Assert.Equal(["Hello, target!"], output);
        }
        finally
        {
            TempDirectories.Delete(root);
        }
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1017")]
    public void TargetOption_UnknownListsTargets()
    {
        var fs = Fs();

        var (code, output, err) = Run(fs, "P.pas", "--rtl", @"C:\rtl", "--emit", "ll", "--target", "nosuch");

        Assert.Equal(1, code);
        Assert.Equal([@"C:\work\P.pas(0,0): error E0011: Unknown target 'nosuch' (available: win64)"], output);
        Assert.Empty(err);
        Assert.DoesNotContain(fs.EnumerateFiles(@"C:\work\obj\Debug", "*"), _ => true);          // nothing was compiled

        // ids are case-sensitive; a missing value is the usual E0006; JSON form carries the same diagnostic and the summary
        Assert.Equal([@"C:\work\P.pas(0,0): error E0011: Unknown target 'WIN64' (available: win64)"], Run(Fs(), "P.pas", "--target", "WIN64").Out);
        Assert.Equal([@"C:\work\P.pas(0,0): error E0006: Missing argument for option '--target'"], Run(Fs(), "P.pas", "--target").Out);
        var json = Run(Fs(), "P.pas", "--target", "win32", "--diagnostics", "json");
        Assert.Equal(1, json.ExitCode);
        Assert.Contains("\"code\":\"E0011\",\"message\":\"Unknown target 'win32' (available: win64)\"", json.Out[0], StringComparison.Ordinal);
        Assert.StartsWith("{\"summary\":{\"errors\":1,", json.Out[1], StringComparison.Ordinal);

        // the known id selects the registry's descriptor and changes nothing else
        var parsed = Parse("P.pas", "--target", "win64");
        Assert.Empty(parsed.Errors);
        Assert.Same(TargetInfo.Win64, parsed.Compilation!.Target);
        Assert.Equal(Parse("P.pas").Compilation, parsed.Compilation);
        Assert.Equal(@"C:\work\obj\Debug", parsed.Compilation.ObjectDirectory);
        Assert.Equal(@"C:\work\obj\Release", Parse("P.pas", "-O2", "--target", "win64").Compilation!.ObjectDirectory);

        // the option is in the help text, after --toolchain
        var help = Run(new InMemoryFileSystem(), "--help").Out;
        var toolchain = Array.FindIndex(help, l => l.StartsWith("  --toolchain <dir>", StringComparison.Ordinal));
        Assert.Equal("  --target <id>         compilation target (default win64; `opc --version` lists the available ids)", help[toolchain + 1]);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1017")]
    public void Version_ListsTargets()
    {
        var (code, output, err) = Run(new InMemoryFileSystem(), "--version");

        Assert.Equal(0, code);
        Assert.Equal(2, output.Length);
        Assert.Matches(@"^opc 0\.1\.0 \(abi 1, llvm \d+\.\d+\.\d+ pin\)$", output[0]);
        Assert.Equal("targets: win64", output[1]);
        Assert.Equal("targets: " + string.Join(", ", TargetRegistry.BuiltIn.Select(t => t.Id)), output[1]);
        Assert.Empty(err);
    }
}
