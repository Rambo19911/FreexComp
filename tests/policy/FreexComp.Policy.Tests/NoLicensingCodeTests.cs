using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace FreexComp.Policy.Tests;

/// <summary>The product has no registration, activation, licence-key, trial or edition logic and no text that suggests one (NET-030…032).</summary>
[Trait("Phase", "8")]
public sealed class NoLicensingCodeTests
{
    private const string ExemptFile = "tests/policy/licensing-terms-exempt.txt";

    /// <summary>One phrase per pattern of the list, in the order of the list.</summary>
    private static readonly string[] Samples =
    [
        "Enter your license key",
        "Serial number:",
        "ProductKey",
        "unlock-code",
        "Activation required",
        "Register your copy",
        "registration_wizard",
        "Trial expired",
        "14 days left in your trial",
        "Professional Edition",
        "licence server",
    ];

    private static readonly string[] OrdinaryWords =
    [
        "window activation", "OnActivate", "WM_ACTIVATE", "Activated", "edition", "the second edition of the form",
        "serialization", "serial port", "number of items", "Apache License 2.0", "license", "licence texts", "trial",
        "registration of the window class", "RegisterClass", "register the component", "product name", "key", "unlock the file",
        "check the box", "days remaining", "enterprise", "community",
    ];

    /// <summary>Tracked files, as paths relative to the repository root with forward slashes.</summary>
    private static IReadOnlyList<string> TrackedFiles()
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoPaths.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in (string[])["-c", "core.quotepath=off", "ls-files", "-z"])
            start.ArgumentList.Add(argument);
        using var git = Process.Start(start) ?? throw new InvalidOperationException("git could not be started");
        var errors = git.StandardError.ReadToEndAsync();
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        Assert.True(git.ExitCode == 0, $"git ls-files failed ({git.ExitCode}): {errors.Result}");
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    [Trait("Req", "NET-030")]
    [Trait("Req", "NET-032")]
    public void NoLicensingTermsInProduct()
    {
        var files = TrackedFiles()
            .Where(LicensingTerms.InScope)
            .Where(path => File.Exists(RepoPaths.Combine(path.Split('/'))))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(files.Count > 100, $"only {files.Count} product files found"); // the scan is not blind

        var exemptPath = RepoPaths.Combine(ExemptFile.Split('/'));
        Assert.True(File.Exists(exemptPath), $"{ExemptFile} is missing");
        var problems = LicensingTerms.Check(
            files.Select(path => (path, File.ReadAllText(RepoPaths.Combine(path.Split('/'))))),
            File.ReadAllText(exemptPath));
        Assert.Empty(problems);
    }

    [Fact]
    [Trait("Req", "NET-030")]
    public void ScopeIsTheProductFolders()
    {
        Assert.All(
            (string[])
            [
                "ide/src/FreexComp.Ide/App.axaml", "ide/src/FreexComp.Ide.Core/Resources/Strings.resx", "compiler/src/opc/Program.cs",
                "rtl/src/System.pas", "rtl/src/win64/start.inc", "ui-framework/design/Forms.components.json", "help/index.md",
                "scripts/bootstrap.ps1", "ide/src/FreexComp.Ide.Core/Templates/console-pascal/Program.pas.txt",
            ],
            path => Assert.True(LicensingTerms.InScope(path), path));
        Assert.All(
            (string[])
            [
                "LICENSE", "THIRD-PARTY-NOTICES.md", "THIRD-PARTY-NOTICES.txt", "README.md", "NETWORK-POLICY.md",
                "ide/src/FreexComp.Ide.Core/Resources/ThirdPartyNotices.json", "ide/tests/FreexComp.Ide.Tests/AboutTests.cs",
                "tests/policy/FreexComp.Policy.Tests/NoLicensingCodeTests.cs", "ide/src/FreexComp.Ide/Assets/app.ico",
                "scripts/verify.sh", "third_party/llvm/LICENSE.TXT", "src/Program.cs",
            ],
            path => Assert.False(LicensingTerms.InScope(path), path));
    }

    [Fact]
    [Trait("Req", "NET-031")]
    public void PatternsMatchSamplePhrasesAndNotOrdinaryWords()
    {
        Assert.Equal(11, LicensingTerms.Patterns.Count);
        Assert.Equal(Samples.Length, LicensingTerms.Patterns.Count);
        for (var i = 0; i < Samples.Length; i++)
        {
            Assert.Matches(new Regex(LicensingTerms.Patterns[i], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), Samples[i]);
            Assert.Single(LicensingTerms.Check([("a/src/Sample.cs", "first line\n" + Samples[i] + "\n")], ""), p => p.StartsWith("a/src/Sample.cs(2): ", StringComparison.Ordinal));
        }

        Assert.All(OrdinaryWords, words => Assert.Empty(LicensingTerms.Check([("a/src/Words.cs", words)], "")));
    }

    [Fact]
    [Trait("Req", "NET-032")]
    public void StaleExemptionFails()
    {
        (string, string)[] files = [("io/src/SerialPort.pas", "unit SerialPort;\n// reads the Serial Number of the adapter\n")];
        Assert.Equal(["io/src/SerialPort.pas(2): Serial Number"], LicensingTerms.Check(files, ""));

        const string exemption = "io/src/SerialPort.pas|Serial Number|hardware serial number of a port adapter";
        Assert.Empty(LicensingTerms.Check(files, exemption + "\n"));

        // an exemption that matches nothing any more, and a line that is not an exemption
        const string stale = "io/src/Gone.pas|product key|file was removed";
        Assert.Equal(["stale exemption: " + stale], LicensingTerms.Check(files, exemption + "\n" + stale + "\n"));
        Assert.Equal(["stale exemption: " + exemption], LicensingTerms.Check([("io/src/SerialPort.pas", "unit SerialPort;\n")], exemption + "\n"));
        Assert.Contains(LicensingTerms.Check(files, exemption + "\nno reason given|x\n"), p => p.StartsWith("malformed exemption", StringComparison.Ordinal));
    }
}
