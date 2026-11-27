namespace FreexComp.Setup.Tests;

/// <summary>What <c>scripts/build-installer.ps1</c> wrote.</summary>
/// <param name="Setup">The setup program with its payload.</param>
/// <param name="PortableZip">The portable zip.</param>
/// <param name="Version">The product version in their names.</param>
/// <param name="Log">What the script printed.</param>
internal sealed record ProductArtifacts(string Setup, string PortableZip, string Version, string Log);

/// <summary>
/// The real product, built once per test run by <c>scripts/build-installer.ps1</c> (INS-001) into its default output
/// folder <c>artifacts\installer</c> of the repository (git-ignored). The script's build folder stays there, so a later
/// run only rebuilds what changed; the staging folder, the setup program and the zip are written anew every time.
/// </summary>
internal static class ProductBuild
{
    /// <summary>A first build publishes the four programs from nothing; a later one takes about a minute.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(25);

    private static readonly Lazy<ProductArtifacts> Built = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    public static ProductArtifacts Artifacts => Built.Value;

    private static ProductArtifacts Build()
    {
        var repository = SetupProgramBuild.RepositoryRoot();
        var output = Path.Combine(repository, "artifacts", "installer");
        var script = Path.Combine(repository, "scripts", "build-installer.ps1");
        var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.True(File.Exists(shell), shell + " is missing");

        // the test's own environment (the script needs dotnet and git), without what the outer build engine left in it
        var environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(e => !((string)e.Key).StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value ?? "", StringComparer.OrdinalIgnoreCase);

        var started = DateTime.UtcNow;
        var result = ChildProgram.Run(shell, ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Output", output],
            repository, environment, Limit);
        Assert.True(result.ExitCode == 0, result.Describe("build-installer.ps1"));

        var setups = Directory.GetFiles(output, "FreexComp-*-win64-setup.exe")
            .Where(f => File.GetLastWriteTimeUtc(f) >= started.AddSeconds(-2)).ToList();
        Assert.True(setups.Count == 1, $"expected one new setup program in {output}, found {setups.Count}\n{result.Output}");
        var setup = setups[0];
        const string suffix = "-win64-setup.exe";
        var name = Path.GetFileName(setup);
        var version = name["FreexComp-".Length..^suffix.Length];
        var zip = Path.Combine(output, $"FreexComp-{version}-win64-portable.zip");
        Assert.True(File.Exists(zip) && File.GetLastWriteTimeUtc(zip) >= started.AddSeconds(-2),
            $"the script wrote no portable zip {zip} (INS-001, INS-020)\n{result.Output}");
        return new ProductArtifacts(setup, zip, version, result.Output);
    }
}
