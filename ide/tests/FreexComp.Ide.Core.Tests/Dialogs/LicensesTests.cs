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
