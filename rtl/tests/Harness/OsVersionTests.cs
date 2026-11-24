using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// os-version.md §3 (task R-228): <c>SysUtils.TOSVersion</c> reports the version of the machine the tests run on. Every
/// expected value is read from the host by the test itself (the version of the test process, its own
/// <c>RtlGetVersion</c> call); no build number is written here.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
public sealed partial class OsVersionTests
{
    private const string NumbersProgram = """
        program OsNumbers;
        uses
          SysUtils;
        begin
          Writeln(TOSVersion.Major, '.', TOSVersion.Minor, '.', TOSVersion.Build);
        end.
        """;

    private const string NameProgram = """
        program OsName;
        uses
          SysUtils;
        begin
          Writeln(TOSVersion.Name);
          Writeln(TOSVersion.ToString);
        end.
        """;

    [GeneratedRegex(@"^Windows( Server)?( \d+)? \(\d+\.\d+\.\d+, x64\)$")]
    private static partial Regex ToStringShape();

    /// <summary>The version of the host as the test process sees it: a .NET 10 process is told the true version.</summary>
    private static string HostNumbers()
    {
        var v = Environment.OSVersion.Version;
        return string.Create(CultureInfo.InvariantCulture, $"{v.Major}.{v.Minor}.{v.Build}");
    }

    /// <summary>RTL-980 (card acceptance 2): the program prints the true <c>Major.Minor.Build</c> of the host.</summary>
    [Fact]
    [Trait("Req", "RTL-980")]
    public async Task ReportsTheRealVersionOfTheHost()
    {
        var output = await BuildAndRunAsync("OsNumbers", NumbersProgram, []);

        Assert.Equal(HostNumbers(), output);
    }

    /// <summary>RTL-982, RTL-985: <c>Name</c> follows the rule for the host's own build number and product type; <c>ToString</c> has the fixed shape.</summary>
    [Fact]
    [Trait("Req", "RTL-982")]
    [Trait("Req", "RTL-985")]
    public async Task NameAndToStringMatchTheHost()
    {
        var info = new OsVersionInfoEx { Size = Marshal.SizeOf<OsVersionInfoEx>() };
        Assert.Equal(284, info.Size);
        Assert.Equal(0, RtlGetVersion(ref info));
        var name = ExpectedName(info.Major, info.Build, info.ProductType);

        var lines = (await BuildAndRunAsync("OsName", NameProgram, [])).Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.Equal(name, lines[0]);
        Assert.Matches(ToStringShape(), lines[1]);
        Assert.Contains(info.Build.ToString(CultureInfo.InvariantCulture), lines[1], StringComparison.Ordinal);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{name} ({info.Major}.{info.Minor}.{info.Build}, x64)"), lines[1]);
    }

    /// <summary>RTL-980: the answer does not depend on the application manifest: with one, and with <c>--manifest none</c>, the same three numbers.</summary>
    [Fact]
    [Trait("Req", "RTL-980")]
    public async Task SameAnswerWithoutManifest()
    {
        var with = await BuildAndRunAsync("OsNumbers", NumbersProgram, ["--manifest", "default"]);
        var without = await BuildAndRunAsync("OsNumbers", NumbersProgram, ["--manifest", "none"]);

        Assert.Equal(with, without);
        Assert.Equal(HostNumbers(), without);
    }

    /// <summary>The rule of RTL-982, written out independently of the RTL.</summary>
    private static string ExpectedName(int major, int build, byte productType)
    {
        if (major != 10)
        {
            return "Windows";
        }

        if (productType == 1)
        {
            return build >= 22000 ? "Windows 11" : "Windows 10";
        }

        return build >= 26100 ? "Windows Server 2025"
            : build >= 20348 ? "Windows Server 2022"
            : build >= 17763 ? "Windows Server 2019"
            : "Windows Server";
    }

    /// <summary>Compiles <paramref name="text"/> against <c>rtl/src</c>, runs it and returns its trimmed output with LF line ends.</summary>
    private static async Task<string> BuildAndRunAsync(string name, string text, string[] extra)
    {
        var setup = RtlSetup.Default;
        string[] required =
            [setup.OpcPath, .. new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" }.Select(t => Path.Combine(setup.ToolchainDirectory, t))];
        var missing = required.Where(p => !File.Exists(p)).ToList();
        Assert.True(missing.Count == 0, $"Toolchain missing: {string.Join(", ", missing)} (run scripts/bootstrap.ps1).");

        var work = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var source = Path.Combine(work, name + ".pas");
            var exe = Path.Combine(work, name + ".exe");
            await File.WriteAllTextAsync(source, text, TestContext.Current.CancellationToken);
            string[] args = [source, "-o", exe, "--obj-dir", Path.Combine(work, "obj"), .. setup.CompilerOptions, .. extra];
            var (exit, output) = await ImportTableTests.RunAsync(setup.OpcPath, GoldenRunner.CommandLine(args, null), work);
            Assert.True(exit == 0, $"opc exit code {exit}\n{output}");

            var (run, printed) = await ImportTableTests.RunAsync(exe, "", work);
            Assert.True(run == 0, $"{name}.exe exit code {run}\n{printed}");
            return printed.ReplaceLineEndings("\n").TrimEnd();
        }
        finally
        {
            GoldenRunner.TryDelete(work);
        }
    }

    /// <summary><c>OSVERSIONINFOEXW</c> (284 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersionInfoEx
    {
        public int Size;
        public int Major;
        public int Minor;
        public int Build;
        public int PlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string ServicePack;
        public ushort ServicePackMajor;
        public ushort ServicePackMinor;
        public ushort SuiteMask;
        public byte ProductType;
        public byte Reserved;
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int RtlGetVersion(ref OsVersionInfoEx info);
}
