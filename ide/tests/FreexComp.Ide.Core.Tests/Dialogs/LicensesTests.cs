using System.Xml.Linq;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Tests.Infrastructure;

namespace FreexComp.Ide.Core.Tests.Dialogs;

[Trait("Phase", "1")]
public sealed class LicensesTests
{
    /// <summary>
    /// UI-DLG-161: every component shipped with the product (tracked <c>THIRD-PARTY-NOTICES.md</c>, section "Shipped with
    /// the product") and every NuGet package referenced by a shipped project (<c>*/src/**/*.csproj</c>, versions from
    /// <c>Directory.Packages.props</c>) has an entry with the same version and license and a license text; no entry is
    /// stale.
    /// </summary>
    [Fact]
    [Trait("Req", "UI-DLG-161")]
    public void EveryDependencyHasNotice()
    {
        var notices = ThirdPartyNotices.LoadEmbedded();
        var root = SpecTables.RepoRootPath();
        Assert.NotEmpty(notices);
        Assert.All(notices, n => Assert.False(string.IsNullOrWhiteSpace(n.Text), n.Name + " has no license text"));

        var rows = SpecTables.Read(Path.Combine(root, "THIRD-PARTY-NOTICES.md"), "Shipped with the product");
        Assert.NotEmpty(rows);
        var matched = new HashSet<ThirdPartyNotice>();
        foreach (var row in rows)
        {
            var component = row["Component"];
            var notice = notices.Where(n => component == n.Name || component.StartsWith(n.Name + " (", StringComparison.Ordinal)
                    || component.StartsWith(n.Name + ",", StringComparison.Ordinal))
                .MaxBy(n => n.Name.Length);
            Assert.True(notice is not null, $"No notice for shipped component '{component}'");
            Assert.Equal((component, row["Version"], row["License"]), (component, notice.Version, notice.License));
            matched.Add(notice);
        }

        Assert.All(notices, n => Assert.True(matched.Contains(n), $"Notice '{n.Name}' is not listed in THIRD-PARTY-NOTICES.md"));

        var versions = XDocument.Load(Path.Combine(root, "Directory.Packages.props")).Descendants("PackageVersion")
            .ToDictionary(e => (string)e.Attribute("Include")!, e => (string)e.Attribute("Version")!, StringComparer.OrdinalIgnoreCase);
        var shipped = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => Path.GetRelativePath(root, p).Split(Path.DirectorySeparatorChar) is [_, "src", ..])
            .SelectMany(p => XDocument.Load(p).Descendants("PackageReference").Select(e => (string)e.Attribute("Include")!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Contains("Avalonia", shipped);
        foreach (var package in shipped)
        {
            var notice = notices.FirstOrDefault(n => n.Packages.Contains(package, StringComparer.OrdinalIgnoreCase));
            Assert.True(notice is not null, $"No notice lists the shipped package '{package}'");
            Assert.Equal((package, versions[package]), (package, notice.Version));
        }
    }

    /// <summary>Build-only packages in the IDE's resolved graph: not shipped, so they need no notice (lead note 2026-11-14).</summary>
    private static readonly string[] BuildOnlyPackages = ["Avalonia.BuildServices"];

    /// <summary>
    /// I-165: every package in the IDE's resolved graph (<c>obj/project.assets.json</c>, direct and transitive) that puts a
    /// runtime, native or runtime-target asset into the output is covered by a notice with the same version. The JSON is
    /// the authority for transitive packages. Build-only packages are excluded explicitly and must really ship nothing.
    /// </summary>
    [Fact]
    [Trait("Req", "UI-DLG-161")]
    public void EveryShippedTransitivePackageHasNotice()
    {
        var notices = ThirdPartyNotices.LoadEmbedded();
        var assetsPath = Path.Combine(SpecTables.RepoRootPath(), "ide", "src", "FreexComp.Ide", "obj", "project.assets.json");
        Assert.True(File.Exists(assetsPath), "Restore the IDE project first: " + assetsPath);
        using var assets = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(assetsPath));

        var packages = new List<(string Id, string Version, bool Shipped)>();
        foreach (var target in assets.RootElement.GetProperty("targets").EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (library.Value.GetProperty("type").GetString() != "package")
                {
                    continue;
                }

                var shipped = new[] { "runtime", "native", "runtimeTargets" }.Any(kind =>
                    library.Value.TryGetProperty(kind, out var files)
                    && files.EnumerateObject().Any(f => !f.Name.EndsWith("/_._", StringComparison.Ordinal)));
                var separator = library.Name.IndexOf('/', StringComparison.Ordinal);
                packages.Add((library.Name[..separator], library.Name[(separator + 1)..], shipped));
            }
        }

        Assert.Contains(packages, p => p.Id == "SkiaSharp" && p.Shipped);
        foreach (var excluded in packages.Where(p => BuildOnlyPackages.Contains(p.Id, StringComparer.OrdinalIgnoreCase)))
        {
            Assert.False(excluded.Shipped, $"Build-only package '{excluded.Id}' puts files into the output");
        }

        var missing = packages
            .Where(p => p.Shipped && !BuildOnlyPackages.Contains(p.Id, StringComparer.OrdinalIgnoreCase))
            .Where(p => !notices.Any(n => n.Version == p.Version && n.Packages.Contains(p.Id, StringComparer.OrdinalIgnoreCase)))
            .Select(p => $"{p.Id} {p.Version}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(missing.Count == 0, "Shipped packages without a notice: " + string.Join(", ", missing));
    }

    /// <summary>
    /// I-166: a shipped package's own notice file (<c>ThirdPartyNotices.txt</c>, <c>THIRD-PARTY-NOTICES.txt</c> in the
    /// package root of the local NuGet folder named by <c>obj/project.assets.json</c>) is part of the text of the notice
    /// covering the package; and a text shared by several components is stored once in the resource (<c>textOf</c>).
    /// </summary>
    [Fact]
    [Trait("Req", "UI-DLG-161")]
    public void PackageNoticeFilesAreIncluded()
    {
        var notices = ThirdPartyNotices.LoadEmbedded();
        var assetsPath = Path.Combine(SpecTables.RepoRootPath(), "ide", "src", "FreexComp.Ide", "obj", "project.assets.json");
        Assert.True(File.Exists(assetsPath), "Restore the IDE project first: " + assetsPath);
        using var assets = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(assetsPath));
        var folders = assets.RootElement.GetProperty("packageFolders").EnumerateObject().Select(f => f.Name).ToList();

        static string Normalize(string text) => text.ReplaceLineEndings("\n").Trim();

        var checkedFiles = new List<string>();
        foreach (var library in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            var separator = library.Name.IndexOf('/', StringComparison.Ordinal);
            var (id, version) = (library.Name[..separator], library.Name[(separator + 1)..]);
            var notice = notices.FirstOrDefault(n => n.Version == version && n.Packages.Contains(id, StringComparer.OrdinalIgnoreCase));
            if (library.Value.GetProperty("type").GetString() != "package" || notice is null)
                continue; // not shipped (EveryShippedTransitivePackageHasNotice covers the rest)
            var relative = library.Value.GetProperty("path").GetString()!;
            foreach (var file in library.Value.GetProperty("files").EnumerateArray().Select(f => f.GetString()!)
                         .Where(f => !f.Contains('/', StringComparison.Ordinal)
                                     && (f.Equals("ThirdPartyNotices.txt", StringComparison.OrdinalIgnoreCase)
                                         || f.Equals("THIRD-PARTY-NOTICES.txt", StringComparison.OrdinalIgnoreCase))))
            {
                var path = folders.Select(f => Path.Combine(f, relative, file)).FirstOrDefault(File.Exists);
                Assert.True(path is not null, $"{id} {version}: {file} not found in the local package folders");
                Assert.True(Normalize(notice.Text).Contains(Normalize(File.ReadAllText(path)), StringComparison.Ordinal),
                    $"The notice '{notice.Name}' does not include {id}/{file}");
                checkedFiles.Add($"{id}/{file}");
            }
        }

        Assert.Contains("CommunityToolkit.Mvvm/ThirdPartyNotices.txt", checkedFiles);
        Assert.Contains("SkiaSharp.NativeAssets.Win32/THIRD-PARTY-NOTICES.txt", checkedFiles);

        // Stored once: no two entries of the resource carry the same text; textOf shares it.
        using var stream = typeof(ThirdPartyNotices).Assembly.GetManifestResourceStream(ThirdPartyNotices.ResourceName)!;
        using var raw = System.Text.Json.JsonDocument.Parse(stream);
        var stored = raw.RootElement.EnumerateArray()
            .Where(e => e.TryGetProperty("text", out _)).Select(e => e.GetProperty("text").GetString()!).ToList();
        Assert.Equal(stored.Count, stored.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(notices.Single(n => n.Name == "SkiaSharp").Text, notices.Single(n => n.Name == "HarfBuzzSharp").Text);
    }

    [Fact]
    [Trait("Req", "UI-DLG-160")]
    public void ComponentsSortedByNameFirstSelected()
    {
        var model = new LicensesViewModel(
        [
            new ThirdPartyNotice("Zeta", "1.0", "MIT", "z text", []),
            new ThirdPartyNotice("Alpha", "2.0", "Apache-2.0", "a text", []),
        ]);

        Assert.Equal(["Alpha", "Zeta"], model.Components.Select(c => c.Name));
        Assert.Equal("Alpha", model.Selected?.Name);
        Assert.Equal("Alpha 2.0 — Apache-2.0" + Environment.NewLine + Environment.NewLine + "a text", model.Text);
        model.Selected = model.Components[1];
        Assert.StartsWith("Zeta 1.0 — MIT", model.Text, StringComparison.Ordinal);
    }
}
