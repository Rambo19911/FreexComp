using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Build;

/// <summary>Resolved toolchain directories; null = not found (IDE-P3-010). The data-access directory arrives in P7.</summary>
public sealed record ToolchainPaths(string? LlvmBinDir, string? RtlDir, string? FrameworkDir);

/// <summary>Where the automatic search looks (IDE-SET-050): <c>FREEXCOMP_HOME</c>, the IDE's directory and <c>PATH</c>.</summary>
public sealed record ToolchainEnvironment(string? FreexCompHome, string IdeDirectory, IReadOnlyList<string> PathDirectories)
{
    /// <summary>The environment of this process (read once by the composition root).</summary>
    public static ToolchainEnvironment FromProcess() => new(
        Environment.GetEnvironmentVariable("FREEXCOMP_HOME") is { Length: > 0 } home ? home : null,
        AppContext.BaseDirectory.TrimEnd('\\', '/'),
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>A folder's check result for the Toolchain Paths page (dialogs.md §9.9): valid or not, and its status message.</summary>
public sealed record FolderCheck(bool Valid, string MessageId, IReadOnlyList<string> Args);

/// <summary>The toolchain directories passed to the build engine (IDE-P3-010…013); the only reader of the <c>toolchain.*</c> keys.</summary>
public interface IToolchainLocator
{
    /// <summary>Each directory: the setting when not empty, else the automatic search (IDE-SET-050).</summary>
    ToolchainPaths Resolve();

    /// <summary>The automatic search only (the Options placeholder "(automatic: …)").</summary>
    ToolchainPaths ResolveAutomatic();

    /// <summary>
    /// IDE-P3-011/012: <c>Msg.Build.ToolchainMissing</c> with the first missing file (its full path, or the bare name when
    /// the folder was not found), checking LLVM only when <paramref name="needsLlvm"/> (Check Syntax needs the RTL only).
    /// </summary>
    OperationResult CheckForBuild(bool needsLlvm);
}

/// <inheritdoc cref="IToolchainLocator"/>
public sealed class ToolchainLocator(ISettingsService settings, IFileSystem fileSystem, ToolchainEnvironment environment) : IToolchainLocator
{
    public static readonly IReadOnlyList<string> LlvmFiles = ["clang.exe", "lld-link.exe", "llvm-dlltool.exe"];
    public static readonly IReadOnlyList<string> RtlFiles = ["System.pas"];
    public static readonly IReadOnlyList<string> FrameworkFiles = [@"design\index.json"];

    /// <summary>The LLVM folder of the repository layout, searched in the IDE directory's ancestors (IDE-SET-050).</summary>
    public const string RepositoryLlvmDir = @"third_party\llvm\bin";

    public ToolchainPaths Resolve()
    {
        var automatic = ResolveAutomatic();
        return new ToolchainPaths(
            Explicit(SettingsCatalog.ToolchainLlvmBinDir) ?? automatic.LlvmBinDir,
            Explicit(SettingsCatalog.ToolchainRtlDir) ?? automatic.RtlDir,
            Explicit(SettingsCatalog.ToolchainFrameworkDir) ?? automatic.FrameworkDir);
    }

    public ToolchainPaths ResolveAutomatic()
    {
        var llvm = First(Candidates(@"llvm\bin", RepositoryLlvmDir), "clang.exe")
            ?? environment.PathDirectories.FirstOrDefault(d => fileSystem.FileExists(Path.Combine(d, "clang.exe")));
        var rtl = First(Candidates(@"rtl\src", @"rtl\src"), "System.pas");
        var framework = First(Candidates("ui-framework", "ui-framework"), @"design\index.json");
        return new ToolchainPaths(llvm, rtl, framework);
    }

    public OperationResult CheckForBuild(bool needsLlvm)
    {
        var paths = Resolve();
        var missing = (needsLlvm ? FirstMissing(paths.LlvmBinDir, LlvmFiles) : null) ?? FirstMissing(paths.RtlDir, RtlFiles);
        return missing is null ? OperationResult.Ok : OperationResult.Fail("Msg.Build.ToolchainMissing", missing);
    }

    /// <summary>
    /// dialogs.md §9.9: a missing folder → <c>Msg.Options.PathNotFound</c>; a missing file → <c>Msg.Options.ToolNotFound</c>
    /// (first missing name); otherwise valid with <c>Msg.Options.ToolchainFound</c> listing the files.
    /// </summary>
    public static FolderCheck ValidateFolder(IFileSystem fileSystem, string folder, IReadOnlyList<string> requiredFiles)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(requiredFiles);
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder) || !fileSystem.DirectoryExists(folder))
            return new FolderCheck(false, "Msg.Options.PathNotFound", []);
        var missing = requiredFiles.FirstOrDefault(f => !fileSystem.FileExists(Path.Combine(folder, f)));
        return missing is null
            ? new FolderCheck(true, "Msg.Options.ToolchainFound", [string.Join(", ", requiredFiles)])
            : new FolderCheck(false, "Msg.Options.ToolNotFound", [missing]);
    }

    private string? Explicit(SettingKey<string> key) => settings.Get(key) is { Length: > 0 } value ? value.Trim() : null;

    /// <summary><c>%FREEXCOMP_HOME%\x</c>, <c>&lt;IDE dir&gt;\x</c>, then <c>&lt;dir&gt;\y</c> for the IDE dir and each ancestor, nearest first.</summary>
    private IEnumerable<string> Candidates(string underHome, string underAncestor)
    {
        if (environment.FreexCompHome is { } home)
            yield return Path.Combine(home, underHome);
        yield return Path.Combine(environment.IdeDirectory, underHome);
        for (var dir = environment.IdeDirectory; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            yield return Path.Combine(dir, underAncestor);
    }

    private string? First(IEnumerable<string> candidates, string marker) =>
        candidates.FirstOrDefault(d => fileSystem.FileExists(Path.Combine(d, marker)));

    private string? FirstMissing(string? folder, IReadOnlyList<string> files) =>
        folder is null ? files[0] : files.Select(f => Path.Combine(folder, f)).FirstOrDefault(p => !fileSystem.FileExists(p));
}
