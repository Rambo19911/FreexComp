namespace FreexComp.Policy.Tests;

/// <summary>
/// The support matrix (PLAT-001, PLAT-002): the one tracked place of its numbers. The platform tests use these
/// constants; no other build number is written into a test.
/// </summary>
internal static class PlatformMatrix
{
    /// <summary>The oldest supported build: Windows 10, version 1809.</summary>
    public const int MinimumBuild = 17763;

    /// <summary>The version number of the release with <see cref="MinimumBuild"/>, as the help page and the system library list write it.</summary>
    public const int MinimumVersion = 1809;

    /// <summary>The first build of Windows 11; desktop systems below it are Windows 10.</summary>
    public const int Windows11FirstBuild = 22000;

    /// <summary>The supported-system identifier that the generated application manifest declares (Windows 10 and Windows 11).</summary>
    public const string SupportedOsId = "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}";
}
