using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace FreexComp.Policy.Tests;

/// <summary>
/// The platform check suite (PLAT-010…026): the support statement in the user help, the system the suite ran on, and
/// what a program built by the product carries (image headers, manifest, imported system libraries, version report,
/// DPI awareness). The numbers of the support matrix come from <see cref="PlatformMatrix"/>.
/// </summary>
[Trait("Phase", "8")]
[Trait("Category", "Platform")]
[Trait("Category", "Toolchain")]
[Collection(PlatformCollection.Name)]
public sealed partial class PlatformTests(PlatformPrograms programs) : IClassFixture<PlatformPrograms>
{
    private const string HelpPage = "help/supported-platforms.md";

    /// <summary>PLAT-005: the requirement line of the read-me.</summary>
    private const string ReadmeLine = "- Windows 10 (version 1809 or later) or Windows 11, x64";

    private static readonly string[] HelpTableRows =
    [
        "| Windows 11 | x64 | Supported |",
        "| Windows 10, version 1809 or later | x64 | Supported |",
        "| Windows Server | — | Not supported yet |",
        "| 32-bit Windows programs | — | Not supported yet |",
    ];

    [Fact]
    [Trait("Req", "PLAT-003")]
    [Trait("Req", "PLAT-005")]
    [Trait("Req", "PLAT-025")]
    public void HelpPageMatchesTheMatrix()
    {
        var path = RepoPaths.Combine(HelpPage.Split('/'));
        Assert.True(File.Exists(path), HelpPage + " is missing");
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], HelpPage + " starts with a byte order mark");
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        Assert.False(text.Contains('\r'), HelpPage + " contains a carriage return");

        var lines = text.Split('\n');
        Assert.Contains("| System | Editions | Status |", lines);
        foreach (var row in HelpTableRows)
        {
            Assert.Contains(row, lines);
        }

        // the statement and the matrix say the same: the Windows 11 test of the page uses the constant of the matrix
        Assert.Contains(PlatformMatrix.MinimumVersion.ToString(CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"TOSVersion.CheckBuild({PlatformMatrix.Windows11FirstBuild})"),
            text,
            StringComparison.Ordinal);
        foreach (var key in (string[])["executionLevel", "dpiAwareness", "customFile"])
        {
            Assert.Contains("`" + key + "`", text, StringComparison.Ordinal);
        }

        foreach (var heading in (string[])["## What a built program declares", "## Checking the system at run time", "## Requirements of the development tools"])
        {
            Assert.Single(lines, l => l == heading);
        }

        Assert.DoesNotContain("http://", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", text, StringComparison.OrdinalIgnoreCase);

        var readme = File.ReadAllText(RepoPaths.Combine("README.md")).ReplaceLineEndings("\n").Split('\n');
        Assert.Single(readme, l => l == ReadmeLine);
    }

    /// <summary>
    /// PLAT-011: the suite states which system it ran on. An x64 process on a 64-bit desktop system inside the matrix; the
    /// line <c>&lt;Name&gt; &lt;major&gt;.&lt;minor&gt;.&lt;build&gt; x64</c> goes to the test output and to <c>artifacts/platform/host.txt</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "PLAT-001")]
    [Trait("Req", "PLAT-011")]
    public void HostIsInTheSupportMatrix()
    {
        Assert.Equal(Architecture.X64, RuntimeInformation.ProcessArchitecture);
        Assert.Equal(Architecture.X64, RuntimeInformation.OSArchitecture);
        Assert.True(Environment.Is64BitOperatingSystem);

        var info = new OsVersionInfoEx { Size = Marshal.SizeOf<OsVersionInfoEx>() };
        Assert.Equal(0, RtlGetVersion(ref info));
        Assert.Equal(10, info.Major);
        Assert.True(info.Build >= PlatformMatrix.MinimumBuild, $"build {info.Build} is older than the minimum {PlatformMatrix.MinimumBuild}");
        Assert.True(info.ProductType == 1, $"product type {info.ProductType}: not a desktop edition (server systems are outside the matrix)");

        // the name rule of the run-time library for desktop systems
        var name = info.Build >= PlatformMatrix.Windows11FirstBuild ? "Windows 11" : "Windows 10";
        var line = string.Create(CultureInfo.InvariantCulture, $"{name} {info.Major}.{info.Minor}.{info.Build} x64");

        TestContext.Current.TestOutputHelper?.WriteLine(line);
        var file = HostFile;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, line + "\n", new UTF8Encoding(false));
        Assert.Equal(line + "\n", File.ReadAllText(file));
    }

    /// <summary>Where <see cref="HostIsInTheSupportMatrix"/> leaves the host line (an untracked build artefact).</summary>
    internal static string HostFile => RepoPaths.Combine("artifacts", "platform", "host.txt");

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int RtlGetVersion(ref OsVersionInfoEx info);

    /// <summary>The version record of the system (284 bytes), filled by <c>RtlGetVersion</c>: the true version, manifest or not.</summary>
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
}

/// <summary>
/// The platform tests and the test of the script that runs them share <c>artifacts/platform/host.txt</c>, so they
/// run one after the other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PlatformCollection
{
    public const string Name = "Platform";
}
