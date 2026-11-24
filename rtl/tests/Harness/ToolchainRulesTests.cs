namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL rules checked by compiling programs against <c>rtl/src</c>: zero warnings in every unit (RTL-003) and the import
/// libraries generated from the <c>.def</c> files and all passed to the linker (RTL-813).
/// </summary>
[Trait("Phase", "2")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
public sealed class ToolchainRulesTests
{
    /// <summary>Every RTL unit except System (implicit), as one uses list.</summary>
    public static string AllUnits() => string.Join(", ",
        Directory.GetFiles(RtlUnitRulesTests.SourceDirectory, "*.pas").Concat(Directory.GetFiles(RtlUnitRulesTests.WinApiDirectory, "*.pas"))
            .Select(Path.GetFileNameWithoutExtension).Where(n => n != "System").Order(StringComparer.Ordinal));

    /// <summary>A unit with a known warning (W1061), so the test shows that warnings of used units are reported.</summary>
    private const string WarningUnit = """
        unit WarnProbe;
        interface
        implementation
        type
          TShape = class
            procedure Draw; virtual; abstract;
          end;
        initialization
          TShape.Create.Free;
        end.
        """;

    /// <summary>
    /// RTL-003: a program that uses every RTL unit compiles with no warning from any of them under the default options;
    /// the one warning reported is the deliberate one of <c>WarnProbe</c>, a unit next to the program.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-003")]
    public async Task RtlUnits_CompileWithoutWarnings()
    {
        await WithWorkDirectoryAsync(async work =>
        {
            await File.WriteAllTextAsync(Path.Combine(work, "WarnProbe.pas"), WarningUnit, TestContext.Current.CancellationToken);
            var program = "program AllUnits;\nuses " + AllUnits() + ", WarnProbe;\nbegin\nend.\n";
            var (exit, output) = await CompileAsync(work, "AllUnits", program, ["-U", work]);
            Assert.True(exit == 0, $"opc exit code {exit}\n{output}");

            var warnings = output.Split('\n').Where(l => l.Contains(": warning ", StringComparison.Ordinal)).Select(l => l.Trim()).ToList();
            Assert.True(warnings.Count == 1 && warnings[0].Contains("WarnProbe.pas(", StringComparison.Ordinal),
                "expected exactly the warning of WarnProbe.pas, got:\n" + string.Join("\n", warnings));
        });
    }

    private const string User32Program = """
        program LinkCheck;
        uses
          Windows;
        var
          B: array[0..1] of WideChar;
        begin
          B[0] := 'a';
          B[1] := #0;
          CharUpperBuffW(@B[0], 1);
          Writeln(B[0]);
        end.
        """;

    /// <summary>
    /// RTL-813: the build generates an import library from each <c>.def</c> file the program needs (into the object
    /// directory) and passes every generated <c>.lib</c> to <c>lld-link</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-813")]
    public async Task ImportLibraries_AreGeneratedAndAllLinked()
    {
        await WithWorkDirectoryAsync(async work =>
        {
            var (exit, output) = await CompileAsync(work, "LinkCheck", User32Program, []);
            Assert.True(exit == 0, $"opc exit code {exit}\n{output}");

            var obj = Path.Combine(work, "obj");
            var libs = Directory.GetFiles(obj, "*.lib", SearchOption.AllDirectories);
            var names = libs.Select(l => Path.GetFileNameWithoutExtension(l).ToLowerInvariant()).ToList();
            Assert.Contains("kernel32", names);
            Assert.Contains("user32", names);
            var defs = Directory.GetFiles(DefFileTests.DefDirectory, "*.def").Select(d => Path.GetFileNameWithoutExtension(d).ToLowerInvariant());
            Assert.All(names, n => Assert.Contains(n, defs));

            var response = await File.ReadAllTextAsync(Path.Combine(obj, "link.rsp"), TestContext.Current.CancellationToken);
            Assert.All(libs, l => Assert.Contains(l, response, StringComparison.OrdinalIgnoreCase));

            var (run, text) = await ImportTableTests.RunAsync(Path.Combine(work, "LinkCheck.exe"), "", work);
            Assert.Equal(0, run);
            Assert.Equal("A", text.TrimEnd());
        });
    }

    /// <summary>Compiles <paramref name="text"/> as <c>&lt;work&gt;/&lt;name&gt;.pas</c> against <c>rtl/src</c>; returns opc's exit code and output.</summary>
    private static async Task<(int ExitCode, string Output)> CompileAsync(string work, string name, string text, string[] extra)
    {
        var setup = RtlSetup.Default;
        var source = Path.Combine(work, name + ".pas");
        await File.WriteAllTextAsync(source, text, TestContext.Current.CancellationToken);
        string[] args =
        [
            source, "-o", Path.Combine(work, name + ".exe"), "--obj-dir", Path.Combine(work, "obj"), .. setup.CompilerOptions, .. extra,
        ];
        return await ImportTableTests.RunAsync(setup.OpcPath, GoldenRunner.CommandLine(args, null), work);
    }

    private static async Task WithWorkDirectoryAsync(Func<string, Task> body)
    {
        var work = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            await body(work);
        }
        finally
        {
            GoldenRunner.TryDelete(work);
        }
    }
}
