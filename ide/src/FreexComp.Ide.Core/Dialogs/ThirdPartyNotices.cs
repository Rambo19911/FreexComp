using System.Text.Json;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// One third-party component of the Licenses dialog (UI-DLG-161). <see cref="Packages"/> lists the NuGet package ids the
/// entry covers (empty for components that are not packages, such as LLVM).
/// </summary>
public sealed record ThirdPartyNotice(string Name, string Version, string License, string Text, IReadOnlyList<string> Packages);

/// <summary>
/// The embedded resource <c>ThirdPartyNotices.json</c> (<c>[{ "name", "version", "license", "text", "packages" }]</c>),
/// kept in step with the tracked <c>THIRD-PARTY-NOTICES.md</c> and <c>Directory.Packages.props</c> by
/// <c>LicensesTests.EveryDependencyHasNotice</c>.
/// </summary>
public static class ThirdPartyNotices
{
    public const string ResourceName = "ThirdPartyNotices.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<ThirdPartyNotice> LoadEmbedded()
    {
        using var stream = typeof(ThirdPartyNotices).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        var entries = JsonSerializer.Deserialize<List<Entry>>(stream, Json) ?? [];
        return [.. entries.Select(e => new ThirdPartyNotice(e.Name, e.Version, e.License, e.Text.ReplaceLineEndings(), e.Packages ?? []))];
    }

    private sealed record Entry(string Name, string Version, string License, string Text, List<string>? Packages);
}
