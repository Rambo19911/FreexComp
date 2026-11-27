using System.Text;
using System.Text.Json;

namespace FreexComp.Policy.Tests;

/// <summary>
/// The dependency licence gate (LIC-070…084): every package of the resolved graph of every solution project declares a
/// licence of the allow-list, every entry of the toolchain manifest names one, and what cannot be read by a machine is
/// listed, with a reason and an exact version, in <c>tests/policy/licence-exceptions.txt</c>. The disallowed cases are
/// proven on a hand-written fixture tree: no real project ever references a package with a disallowed licence.
/// </summary>
[Trait("Phase", "8")]
public sealed class DependencyLicenceTests
{
    private const string FixtureObj = "obj-fixture";

    private static readonly string FixtureTree = RepoPaths.Combine("tests", "policy", "Fixtures", "licences", "tree");
    private static readonly string ExceptionsFile = RepoPaths.Combine(DependencyLicences.ExceptionsRelative.Split('/'));

    private const string Copyleft = "Contoso.Copyleft 1.0.0: licence 'GPL-3.0-only' is not allowed (used by App.csproj)";
    private const string NoExpression = "Contoso.NoExpression 1.0.0: declares no licence expression (used by App.csproj); review it and add it to tests/policy/licence-exceptions.txt";
    private const string Transitive = "Contoso.Transitive 2.0.0: licence 'LGPL-2.1-only' is not allowed (used by App.csproj)";

    private const string Reason = "read in the package folder; permissive text";

    private static string Lines(IEnumerable<string> problems) => string.Join("\n", problems);

    private static string PathIn(DirectoryInfo temp, string relative) => Path.Combine([temp.FullName, .. relative.Split('/')]);

    private static void Write(DirectoryInfo temp, string relative, string text)
    {
        var path = PathIn(temp, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>A temporary copy of the fixture tree, with the given lines as its exceptions file.</summary>
    private static DirectoryInfo FixtureCopy(params string[] exceptions)
    {
        var temp = Directory.CreateTempSubdirectory("policy-licences-");
        foreach (var file in Directory.EnumerateFiles(FixtureTree, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(temp.FullName, Path.GetRelativePath(FixtureTree, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        if (exceptions.Length > 0)
        {
            Write(temp, DependencyLicences.ExceptionsRelative, string.Join("\n", exceptions) + "\n");
        }

        return temp;
    }

    private static IReadOnlyList<string> FixtureProblems(DirectoryInfo temp) =>
        DependencyLicences.Check(temp.FullName, toolchain: false, objFolder: FixtureObj).Problems;

    /// <summary>A temporary tree with the real toolchain manifest, notices table and exceptions file.</summary>
    private static DirectoryInfo ToolchainCopy()
    {
        var temp = Directory.CreateTempSubdirectory("policy-licences-");
        try
        {
            foreach (var relative in (string[])[DependencyLicences.ToolchainRelative, DependencyLicences.NoticesName, DependencyLicences.ExceptionsRelative])
            {
                Write(temp, relative, File.ReadAllText(RepoPaths.Combine(relative.Split('/'))));
            }
        }
        catch
        {
            temp.Delete(recursive: true); // a missing input fails the test and leaves nothing behind
            throw;
        }

        return temp;
    }

    private static IReadOnlyList<string> ToolchainProblems(DirectoryInfo temp) =>
        DependencyLicences.Check(temp.FullName, packages: false).Problems;

    /// <summary>Replaces text of a file of a temporary tree; the text must be there, so a test cannot pass by changing nothing.</summary>
    private static void Replace(DirectoryInfo temp, string relative, string old, string replacement)
    {
        var path = PathIn(temp, relative);
        var text = File.ReadAllText(path);
        Assert.Contains(old, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(old, replacement, StringComparison.Ordinal), new UTF8Encoding(false));
    }

    // ---------------------------------------------------------------- packages (L-026)

    [Fact]
    [Trait("Req", "LIC-070")]
    [Trait("Req", "LIC-071")]
    [Trait("Req", "LIC-074")]
    [Trait("Req", "LIC-081")]
    [Trait("Req", "LIC-082")]
    public void CurrentSolutionPasses()
    {
        var report = DependencyLicences.Check(RepoPaths.Root, toolchain: false);

        Assert.True(report.Problems.Count == 0, Lines(report.Problems));

        // the check was not blind: it read packages with an expression, and the reviewed entries were looked at
        Assert.True(report.Packages.Count >= 20, $"only {report.Packages.Count} packages found");
        Assert.Contains(report.Packages, p => p.Expression == "MIT");
        Assert.All(report.Packages, p => Assert.NotEmpty(p.Projects));
        Assert.All(report.Packages.Where(p => p.Expression is null), p =>
            Assert.Contains(report.Exceptions, e => e.Kind == "package" && e.Version == p.Version && string.Equals(e.Name, p.Id, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    [Trait("Req", "LIC-070")]
    public void EverySolutionProjectIsChecked()
    {
        // the project files, read here from the solution text independently of the checker
        var expected = File.ReadAllLines(RepoPaths.Combine("FreexComp.sln"))
            .Where(l => l.StartsWith("Project(", StringComparison.Ordinal))
            .Select(l => l.Split('"')[5].Replace('\\', '/'))
            .Where(p => p.EndsWith(".csproj", StringComparison.Ordinal))
            .Select(p => p[..(p.LastIndexOf('/') + 1)] + "obj/project.assets.json")
            .ToList();
        Assert.True(expected.Count >= 26, $"only {expected.Count} projects in the solution");
        Assert.Contains("tests/policy/FreexComp.Policy.Tests/obj/project.assets.json", expected);
        Assert.Contains("ide/src/FreexComp.Ide/obj/project.assets.json", expected);

        var report = DependencyLicences.Check(RepoPaths.Root, toolchain: false);

        Assert.Equal(expected, report.AssetsFiles);
        Assert.All(report.AssetsFiles, f => Assert.True(File.Exists(RepoPaths.Combine(f.Split('/'))), f));
    }

    [Fact]
    [Trait("Req", "LIC-074")]
    [Trait("Req", "LIC-083")]
    [Trait("Req", "LIC-084")]
    public void CopyleftPackageIsReportedWithItsName()
    {
        // the fixture as it is tracked: read in place, under the folder name the repository does not ignore
        var report = DependencyLicences.Check(FixtureTree, toolchain: false, objFolder: FixtureObj);

        Assert.Equal([Copyleft, NoExpression, Transitive], report.Problems);
        Assert.Equal(["App/obj-fixture/project.assets.json"], report.AssetsFiles);
        Assert.Equal(
            ["Contoso.Copyleft", "Contoso.Dual", "Contoso.Fine", "Contoso.NoExpression", "Contoso.Transitive"],
            report.Packages.Select(p => p.Id)); // the project reference among the libraries is no package
        Assert.Equal("GPL-2.0-only OR MIT", report.Packages[1].Expression);

        // the transitive package is named by no project: only another package depends on it
        using (var assets = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureTree, "App", FixtureObj, "project.assets.json"))))
        {
            var direct = assets.RootElement.GetProperty("projectFileDependencyGroups").GetProperty("net10.0").EnumerateArray().Select(d => d.GetString()!).ToList();
            Assert.Contains(direct, d => d.StartsWith("Contoso.Copyleft ", StringComparison.Ordinal));
            Assert.DoesNotContain(direct, d => d.StartsWith("Contoso.Transitive", StringComparison.Ordinal));
        }

        // a package used by more than three projects: the list is in solution order and cut after three names
        var temp = FixtureCopy();
        try
        {
            var solution = PathIn(temp, "Fixture.sln");
            var line = File.ReadAllLines(solution).Single(l => l.StartsWith("Project(", StringComparison.Ordinal));
            foreach (var name in (string[])["Zeta", "Beta", "Gamma"])
            {
                File.AppendAllText(solution, line.Replace("App", name, StringComparison.Ordinal) + "\r\nEndProject\r\n");
                Write(temp, name + "/obj-fixture/project.assets.json", File.ReadAllText(PathIn(temp, "App/obj-fixture/project.assets.json")));
            }

            Assert.Equal(
                [
                    Copyleft.Replace("App.csproj", "App.csproj, Zeta.csproj, Beta.csproj, …", StringComparison.Ordinal),
                    NoExpression.Replace("App.csproj", "App.csproj, Zeta.csproj, Beta.csproj, …", StringComparison.Ordinal),
                    Transitive.Replace("App.csproj", "App.csproj, Zeta.csproj, Beta.csproj, …", StringComparison.Ordinal),
                ],
                FixtureProblems(temp));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Theory]
    [Trait("Req", "LIC-072")]
    [Trait("Req", "LIC-073")]
    [InlineData("MIT", true)]
    [InlineData("Apache-2.0", true)]
    [InlineData("Apache-2.0 WITH LLVM-exception", true)]
    [InlineData("BSD-2-Clause", true)]
    [InlineData("BSD-3-Clause", true)]
    [InlineData("Zlib", true)]
    [InlineData("ISC", true)]
    [InlineData("  MIT  ", true)]
    [InlineData("MIT OR GPL-3.0-only", true)]
    [InlineData("GPL-3.0-only OR MIT", true)]
    [InlineData("MIT AND GPL-3.0-only", false)]
    [InlineData("MIT AND Apache-2.0", true)]
    [InlineData("(MIT OR Apache-2.0) AND BSD-3-Clause", true)]
    [InlineData("(MIT OR GPL-3.0-only) AND LGPL-2.1-only", false)]
    [InlineData("GPL-3.0-only OR MIT AND LGPL-2.1-only", false)] // AND binds tighter than OR
    [InlineData("MIT OR GPL-3.0-only AND LGPL-2.1-only", true)]
    [InlineData("MIT OR Apache-2.0 WITH LLVM-exception", true)]
    [InlineData("GPL-2.0-only WITH Classpath-exception-2.0", false)]
    [InlineData("MIT WITH LLVM-exception", false)]
    [InlineData("GPL-3.0-only", false)]
    [InlineData("GPL-3.0-or-later", false)]
    [InlineData("LGPL-2.1-only", false)]
    [InlineData("Apache-2.0+", false)]
    [InlineData("PSF-2.0", false)]
    [InlineData("Public domain", false)]
    [InlineData("mit", false)]
    [InlineData("MIT or Apache-2.0", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("MIT OR", false)]
    [InlineData("OR MIT", false)]
    [InlineData("MIT Apache-2.0", false)]
    [InlineData("(MIT", false)]
    [InlineData("MIT)", false)]
    [InlineData("()", false)]
    [InlineData("MIT WITH", false)]
    [InlineData("MIT, Apache-2.0", false)]
    [InlineData("MIT XOR Apache-2.0", false)]
    public void ExpressionsAreEvaluated(string? expression, bool allowed)
    {
        Assert.Equal(allowed, DependencyLicences.IsAllowed(expression));
    }

    [Fact]
    [Trait("Req", "LIC-072")]
    public void AllowListIsTheReviewedOne()
    {
        Assert.Equal(
            ["MIT", "Apache-2.0", "Apache-2.0 WITH LLVM-exception", "BSD-2-Clause", "BSD-3-Clause", "Zlib", "ISC"],
            DependencyLicences.AllowedLicences);
        Assert.All(DependencyLicences.AllowedLicences, l => Assert.True(DependencyLicences.IsAllowed(l), l));
    }

    [Fact]
    [Trait("Req", "LIC-071")]
    public void MissingRestoreOutputIsAFailureNotASkip()
    {
        const string assets = "App/obj-fixture/project.assets.json";

        // no assets file
        var temp = FixtureCopy();
        try
        {
            File.Delete(PathIn(temp, assets));
            Assert.Equal(
                [$"{assets}: the restore output of App.csproj is missing; run 'dotnet restore FreexComp.sln' first"],
                FixtureProblems(temp));
        }
        finally
        {
            temp.Delete(recursive: true);
        }

        // the default folder name is "obj": the fixture has none, so the same tree fails without the parameter
        Assert.Equal(
            ["App/obj/project.assets.json: the restore output of App.csproj is missing; run 'dotnet restore FreexComp.sln' first"],
            DependencyLicences.Check(FixtureTree, toolchain: false).Problems);

        // an assets file that cannot be parsed, and one without the expected members
        foreach (var broken in (string[])["{ \"libraries\": ", "{ \"version\": 3 }"])
        {
            temp = FixtureCopy();
            try
            {
                Write(temp, assets, broken);
                var problem = Assert.Single(FixtureProblems(temp));
                Assert.StartsWith($"{assets}: cannot be read (", problem, StringComparison.Ordinal);
                Assert.EndsWith("; run 'dotnet restore FreexComp.sln' first", problem, StringComparison.Ordinal);
            }
            finally
            {
                temp.Delete(recursive: true);
            }
        }

        // a package whose metadata file is in none of the package folders: reported, and not taken for a package that passes
        temp = FixtureCopy();
        try
        {
            File.Delete(PathIn(temp, "packages/contoso.fine/1.0.0/contoso.fine.nuspec"));
            Assert.Equal(
                [
                    $"Contoso.Fine 1.0.0: metadata file contoso.fine/1.0.0/contoso.fine.nuspec is in none of the package folders of {assets}; run 'dotnet restore FreexComp.sln' first",
                    Copyleft, NoExpression, Transitive,
                ],
                FixtureProblems(temp));
        }
        finally
        {
            temp.Delete(recursive: true);
        }

        // a tree without a solution file is not an empty, passing tree
        temp = Directory.CreateTempSubdirectory("policy-licences-");
        try
        {
            Assert.Equal(["expected one solution file in the root directory, found 0"], FixtureProblems(temp));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    // ---------------------------------------------------------------- exceptions file (L-026)

    [Fact]
    [Trait("Req", "LIC-079")]
    [Trait("Req", "LIC-082")]
    public void ExceptionsFileIsWellFormed()
    {
        Assert.True(File.Exists(ExceptionsFile), DependencyLicences.ExceptionsRelative + " is missing");
        var (entries, problems) = DependencyLicences.ReadExceptions(ExceptionsFile);
        Assert.True(problems.Count == 0, Lines(problems));
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.True(e.Justification.Length >= 20, $"line {e.Line}"));
        var header = File.ReadAllLines(ExceptionsFile).TakeWhile(l => l.StartsWith('#')).ToList();
        Assert.Contains(header, l => l.Contains("added by a maintainer only", StringComparison.Ordinal));

        var temp = Directory.CreateTempSubdirectory("policy-licences-");
        try
        {
            var file = Path.Combine(temp.FullName, DependencyLicences.ExceptionsName);
            File.WriteAllText(file, string.Join("\n",
                "# comment",
                "",
                $"package | Contoso.A | 1.0.0 | MIT | {Reason}",
                $"package | Contoso.B | 1.0.0 | {Reason}",
                $"package | Contoso.C |  | MIT | {Reason}",
                $"library | Contoso.D | 1.0.0 | MIT | {Reason}",
                "package | Contoso.E | 1.0.0 | MIT | short reason",
                $"toolchain | tool | 2.0 | ISC | {Reason}",
                $"package|Contoso.F|1.0.0|MIT|{Reason}",
                ""));
            (entries, problems) = DependencyLicences.ReadExceptions(file);

            Assert.Equal(
                [
                    "licence-exceptions.txt(4): expected five cells '<kind> | <name> | <version> | <licence> | <justification>', found 4",
                    "licence-exceptions.txt(5): empty cell",
                    "licence-exceptions.txt(6): unknown kind 'library'",
                    "licence-exceptions.txt(7): the justification has fewer than 20 characters",
                    "licence-exceptions.txt(9): expected five cells '<kind> | <name> | <version> | <licence> | <justification>', found 1",
                ],
                problems);
            Assert.Equal(
                [
                    new DependencyLicences.Excepted(3, "package", "Contoso.A", "1.0.0", "MIT", Reason),
                    new DependencyLicences.Excepted(8, "toolchain", "tool", "2.0", "ISC", Reason),
                ],
                entries);

            // the encoding and the line ends are part of the format
            File.WriteAllBytes(file, [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes($"package | Contoso.A | 1.0.0 | MIT | {Reason}\r\n")]);
            (entries, problems) = DependencyLicences.ReadExceptions(file);
            Assert.Equal(
                ["licence-exceptions.txt: must be UTF-8 without a byte order mark", "licence-exceptions.txt: must use LF line endings"],
                problems);
            Assert.Single(entries);
        }
        finally
        {
            temp.Delete(recursive: true);
        }

        // a malformed line of the file fails the check of a tree
        temp = FixtureCopy($"package | Contoso.NoExpression | 1.0.0 | {Reason}");
        try
        {
            Assert.Contains(
                "licence-exceptions.txt(1): expected five cells '<kind> | <name> | <version> | <licence> | <justification>', found 4",
                FixtureProblems(temp));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Theory]
    [Trait("Req", "LIC-080")]
    [InlineData("GPL-3.0-only", false)]
    [InlineData("GPL-2.0-only WITH Classpath-exception-2.0", false)]
    [InlineData("AGPL-3.0-only", false)]
    [InlineData("SSPL-1.0", false)]
    [InlineData("EUPL-1.2", false)]
    [InlineData("OSL-3.0", false)]
    [InlineData("CC-BY-NC-4.0", false)]
    [InlineData("CC-BY-NC-SA-4.0", false)]
    [InlineData("CC-BY-ND-4.0", false)]
    [InlineData("BUSL-1.1", false)]
    [InlineData("LGPL-2.1-only", false)]
    [InlineData("LGPL-3.0-or-later", false)]
    [InlineData("MIT OR GPL-3.0-only", false)]
    [InlineData("(MIT AND LGPL-2.1-only)", false)]
    [InlineData("gpl-3.0-only", false)]
    [InlineData("Proprietary", false)]
    [InlineData("proprietary terms of the vendor", false)]
    [InlineData("Commercial", false)]
    [InlineData("COMMERCIAL licence", false)]
    [InlineData("LGPL-2.1-only WITH OCaml-LGPL-linking-exception", true)]
    [InlineData("BSD-3-Clause", true)]
    [InlineData("PSF-2.0", true)]
    [InlineData("HPND", true)]
    public void ExceptionCannotAdmitCopyleft(string licence, bool accepted)
    {
        Assert.Equal(accepted, DependencyLicences.CanBeExcepted(licence));

        // through the file: the entry is refused with its line number, and it admits nothing
        var temp = FixtureCopy("# reviewed", $"package | Contoso.NoExpression | 1.0.0 | {licence} | {Reason}");
        try
        {
            var problems = FixtureProblems(temp);
            if (accepted)
            {
                Assert.DoesNotContain(problems, p => p.Contains("cannot be excepted", StringComparison.Ordinal));
                Assert.DoesNotContain(NoExpression, problems);
            }
            else
            {
                Assert.Contains($"licence-exceptions.txt(2): '{licence}' cannot be excepted", problems);
                Assert.Contains(NoExpression, problems);
            }
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "LIC-081")]
    public void StaleIdleAndContradictingExceptionsAreReported()
    {
        var temp = FixtureCopy(
            $"package | Contoso.Gone | 1.0.0 | BSD-3-Clause | {Reason}",
            $"package | Contoso.Fine | 1.0.0 | MIT | {Reason}",
            $"package | Contoso.NoExpression | 1.0.0 | ISC | {Reason}",
            $"package | contoso.noexpression | 1.0.0 | BSD-3-Clause | {Reason}",
            $"package | Contoso.Dual | 1.0.0 | MIT | {Reason}",
            $"toolchain | sqlite | 1.0.0 | Zlib | {Reason}");
        try
        {
            Assert.Equal(
                [
                    Copyleft,
                    Transitive,
                    "licence-exceptions.txt(1): package Contoso.Gone 1.0.0: no such dependency",
                    "licence-exceptions.txt(2): package Contoso.Fine 1.0.0: not needed",
                    "licence-exceptions.txt(3): package Contoso.NoExpression 1.0.0: differs from THIRD-PARTY-NOTICES.md ('BSD-3-Clause')",
                    "licence-exceptions.txt(4): package contoso.noexpression 1.0.0: duplicate of line 3",
                    "licence-exceptions.txt(5): package Contoso.Dual 1.0.0: not needed",
                ],
                FixtureProblems(temp)); // the toolchain entry is not judged: this run does not look at a toolchain manifest

            // with the toolchain part, an entry for a key the manifest does not have is stale as well
            Write(temp, DependencyLicences.ToolchainRelative, "{ \"sqlite\": { \"version\": \"2.0.0\", \"license\": \"Public domain\" }, \"dotnetSdk\": \"10.0\" }");
            File.AppendAllText(PathIn(temp, DependencyLicences.NoticesName), "\n## Shipped with the product\n| Component | Version | License | Source |\n|---|---|---|---|\n| SQLite | 2.0.0 | Public domain | none |\n");
            var problems = DependencyLicences.Check(temp.FullName, objFolder: FixtureObj).Problems;
            Assert.Contains("licence-exceptions.txt(6): toolchain sqlite 1.0.0: no such dependency", problems);
            Assert.Equal(8, problems.Count);

            Replace(temp, DependencyLicences.ExceptionsRelative, "sqlite | 1.0.0", "sqlite | 2.0.0");
            problems = DependencyLicences.Check(temp.FullName, objFolder: FixtureObj).Problems;
            Assert.Contains("licence-exceptions.txt(6): toolchain sqlite 2.0.0: not needed", problems);
            Assert.Contains("licence-exceptions.txt(6): toolchain sqlite 2.0.0: differs from THIRD-PARTY-NOTICES.md ('Public domain')", problems);
            Assert.Equal(9, problems.Count);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "LIC-074")]
    [Trait("Req", "LIC-079")]
    public void ExceptionAdmitsThePackageWithoutExpression()
    {
        // the package id is compared ignoring case; the version is exact
        var temp = FixtureCopy("# reviewed entries", "", $"package | contoso.NOEXPRESSION | 1.0.0 | BSD-3-Clause | {Reason}");
        try
        {
            Assert.Equal([Copyleft, Transitive], FixtureProblems(temp));

            Replace(temp, DependencyLicences.ExceptionsRelative, "| 1.0.0 |", "| 1.0.1 |");
            Assert.Equal(
                [Copyleft, NoExpression, Transitive, "licence-exceptions.txt(3): package contoso.NOEXPRESSION 1.0.1: no such dependency"],
                FixtureProblems(temp));
        }
        finally
        {
            temp.Delete(recursive: true);
        }

        // an expression that is not allowed can be excepted too, unless it is copyleft
        temp = FixtureCopy(
            $"package | Contoso.NoExpression | 1.0.0 | BSD-3-Clause | {Reason}",
            $"package | Contoso.Copyleft | 1.0.0 | GPL-3.0-only | {Reason}");
        try
        {
            Replace(temp, "packages/contoso.transitive/2.0.0/contoso.transitive.nuspec", "LGPL-2.1-only", "HPND");
            Assert.Equal(
                [Copyleft, "Contoso.Transitive 2.0.0: licence 'HPND' is not allowed (used by App.csproj)", "licence-exceptions.txt(2): 'GPL-3.0-only' cannot be excepted"],
                FixtureProblems(temp));

            File.AppendAllText(PathIn(temp, DependencyLicences.ExceptionsRelative), $"package | Contoso.Transitive | 2.0.0 | HPND | {Reason}\n");
            Assert.Equal([Copyleft, "licence-exceptions.txt(2): 'GPL-3.0-only' cannot be excepted"], FixtureProblems(temp));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    // ---------------------------------------------------------------- toolchain manifest (L-027)

    [Fact]
    [Trait("Req", "LIC-076")]
    [Trait("Req", "LIC-077")]
    [Trait("Req", "LIC-082")]
    public void ToolchainEntriesDeclareAnAllowedLicence()
    {
        var report = DependencyLicences.Check(RepoPaths.Root, packages: false);

        Assert.True(report.Problems.Count == 0, Lines(report.Problems));

        // every object entry of the manifest was looked at, and each one names a licence
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(RepoPaths.Combine("scripts", "toolchain.json")));
        var keys = manifest.RootElement.EnumerateObject().Where(e => e.Value.ValueKind == JsonValueKind.Object).Select(e => e.Name).ToList();
        Assert.Equal(keys, report.Tools.Select(t => t.Key));
        Assert.True(keys.Count >= 3, $"only {keys.Count} toolchain entries");
        Assert.All(report.Tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Licence), t.Key));
        Assert.Equal("Apache-2.0 WITH LLVM-exception", report.Tools.Single(t => t.Key == "llvm").Licence);
        Assert.Equal(DependencyLicences.PublicDomain, report.Tools.Single(t => t.Key == "sqlite").Licence);

        // an entry that is not on the allow-list has its reviewed exception, bound to the version in use
        var python = report.Tools.Single(t => t.Key == "python");
        Assert.False(DependencyLicences.IsAllowed(python.Licence));
        Assert.Contains(report.Exceptions, e => e.Kind == "toolchain" && e.Name == "python" && e.Version == python.Version && e.Licence == python.Licence);
    }

    /// <summary>
    /// L-046: the Unicode Character Database file the character tables are generated from is a pinned build input —
    /// an entry of the manifest with its licence, a reviewed exception bound to its version, a download by
    /// <c>scripts/bootstrap.ps1</c> only, and a local copy whose bytes have the pinned hash. Fails (never skips) when the
    /// copy is missing; reads local files only.
    /// </summary>
    [Fact]
    [Trait("Req", "LIC-076")]
    [Trait("Req", "LIC-077")]
    public void UnicodeDataIsAPinnedBuildInput()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(RepoPaths.Combine("scripts", "toolchain.json")));
        Assert.True(manifest.RootElement.TryGetProperty("unicode", out var entry), "scripts/toolchain.json has no entry 'unicode'");
        var version = entry.GetProperty("version").GetString()!;
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
        Assert.Equal("Unicode-3.0", entry.GetProperty("license").GetString());
        Assert.Equal($"https://www.unicode.org/Public/{version}/ucd/UnicodeData.txt", entry.GetProperty("url").GetString());
        var pinned = entry.GetProperty("sha256").GetString()!;
        Assert.Matches("^[0-9a-f]{64}$", pinned);
        Assert.Equal(["UnicodeData.txt"], entry.GetProperty("requiredFiles").EnumerateArray().Select(f => f.GetString()!));

        // not on the allow-list of what the product may depend on: it passes with its reviewed exception only
        var report = DependencyLicences.Check(RepoPaths.Root, packages: false);
        Assert.True(report.Problems.Count == 0, Lines(report.Problems));
        var tool = report.Tools.Single(t => t.Key == "unicode");
        Assert.False(DependencyLicences.IsAllowed(tool.Licence));
        Assert.Contains(report.Exceptions, e => e.Kind == "toolchain" && e.Name == "unicode" && e.Version == version && e.Licence == "Unicode-3.0");

        // the one script that may download fetches it with the pinned hash
        Assert.Contains("Get-Package $cfg.unicode.package $cfg.unicode.url $cfg.unicode.sha256",
            File.ReadAllText(RepoPaths.Combine("scripts", "bootstrap.ps1")), StringComparison.Ordinal);

        var file = Path.Combine([RepoPaths.Root, .. entry.GetProperty("installDir").GetString()!.Split('/'), "UnicodeData.txt"]);
        Assert.True(File.Exists(file), "missing " + file + " (run scripts/bootstrap.ps1)");
        Assert.Equal(pinned, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))));
    }

    [Fact]
    [Trait("Req", "LIC-076")]
    [Trait("Req", "LIC-077")]
    public void ToolchainEntryWithoutOrWithDisallowedLicenceIsReported()
    {
        var temp = ToolchainCopy();
        try
        {
            Assert.Empty(ToolchainProblems(temp));

            // the member removed
            Replace(temp, DependencyLicences.ToolchainRelative, "    \"license\": \"Public domain\",\n", "");
            Assert.Equal(["toolchain.json: entry 'sqlite' has no licence"], ToolchainProblems(temp));

            // an empty value is no licence either
            Replace(temp, DependencyLicences.ToolchainRelative, "\"license\": \"Apache-2.0 WITH LLVM-exception\"", "\"license\": \" \"");
            Assert.Equal(["toolchain.json: entry 'llvm' has no licence", "toolchain.json: entry 'sqlite' has no licence"], ToolchainProblems(temp));
        }
        finally
        {
            temp.Delete(recursive: true);
        }

        // a licence that is not allowed
        temp = ToolchainCopy();
        try
        {
            Replace(temp, DependencyLicences.ToolchainRelative, "\"license\": \"Apache-2.0 WITH LLVM-exception\"", "\"license\": \"GPL-3.0-only\"");
            Assert.Equal(
                [
                    "toolchain.json: entry 'llvm': licence 'GPL-3.0-only' is not allowed",
                    "toolchain.json: entry 'llvm' says 'GPL-3.0-only' but THIRD-PARTY-NOTICES.md says 'Apache-2.0 WITH LLVM-exception'",
                ],
                ToolchainProblems(temp));
        }
        finally
        {
            temp.Delete(recursive: true);
        }

        // a licence outside the allow-list without its exception entry; an entry for another version does not count
        temp = ToolchainCopy();
        try
        {
            var exceptions = PathIn(temp, DependencyLicences.ExceptionsRelative);
            var lines = File.ReadAllLines(exceptions);
            var entry = Assert.Single(lines, l => l.StartsWith("toolchain | python | ", StringComparison.Ordinal));
            File.WriteAllText(exceptions, string.Join("\n", lines.Where(l => l != entry)) + "\n");
            Assert.Equal(["toolchain.json: entry 'python': licence 'PSF-2.0' is not allowed"], ToolchainProblems(temp));

            // the other entries stay as they are (line 1 is the changed one)
            File.WriteAllText(exceptions, string.Join("\n",
                [$"toolchain | python | 0.0.1 | PSF-2.0 | {Reason}", .. lines.Where(l => l != entry && l.Length > 0 && !l.StartsWith('#'))]) + "\n");
            Assert.Equal(
                ["toolchain.json: entry 'python': licence 'PSF-2.0' is not allowed", "licence-exceptions.txt(1): toolchain python 0.0.1: no such dependency"],
                ToolchainProblems(temp));
        }
        finally
        {
            temp.Delete(recursive: true);
        }

        // the member has its place after the version; a missing manifest is a failure
        temp = ToolchainCopy();
        try
        {
            Replace(temp, DependencyLicences.ToolchainRelative, "    \"license\": \"Public domain\",\n", "");
            Replace(temp, DependencyLicences.ToolchainRelative, "\"requiredFiles\": [\"sqlite3.dll\"", "\"license\": \"Public domain\",\n    \"requiredFiles\": [\"sqlite3.dll\"");
            Assert.Equal(["toolchain.json: entry 'sqlite': 'license' must follow 'version'"], ToolchainProblems(temp));

            File.Delete(PathIn(temp, DependencyLicences.ToolchainRelative));
            Assert.Contains("scripts/toolchain.json is missing", ToolchainProblems(temp));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "LIC-078")]
    public void ToolchainLicenceMatchesTheNoticesTable()
    {
        // the real tree: no finding of this kind (the whole toolchain part is clean)
        Assert.DoesNotContain(DependencyLicences.Check(RepoPaths.Root, packages: false).Problems, p => p.Contains(DependencyLicences.NoticesName, StringComparison.Ordinal));

        var temp = ToolchainCopy();
        try
        {
            Replace(temp, DependencyLicences.NoticesName, "| Public domain |", "| Unlicense |");
            Assert.Equal(
                ["toolchain.json: entry 'sqlite' says 'Public domain' but THIRD-PARTY-NOTICES.md says 'Unlicense'"],
                ToolchainProblems(temp));

            // the exception entry of an entry is held against the same row
            Replace(temp, DependencyLicences.NoticesName, "| PSF-2.0 |", "| Python-2.0 |");
            var problems = ToolchainProblems(temp);
            Assert.Contains("toolchain.json: entry 'python' says 'PSF-2.0' but THIRD-PARTY-NOTICES.md says 'Python-2.0'", problems);
            Assert.Contains(problems, p => p.StartsWith("licence-exceptions.txt(", StringComparison.Ordinal)
                && p.EndsWith(": differs from THIRD-PARTY-NOTICES.md ('Python-2.0')", StringComparison.Ordinal));
            Assert.Equal(3, problems.Count);

            // an entry without a row, and the table missing altogether
            Replace(temp, DependencyLicences.NoticesName, "| SQLite |", "| Another library |");
            Assert.Contains("toolchain.json: entry 'sqlite' has no row in THIRD-PARTY-NOTICES.md", ToolchainProblems(temp));
            File.Delete(PathIn(temp, DependencyLicences.NoticesName));
            Assert.Contains("THIRD-PARTY-NOTICES.md is missing", ToolchainProblems(temp));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }
}
