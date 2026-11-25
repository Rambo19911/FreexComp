using System.Collections.Immutable;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Build;

/// <summary>What <see cref="DatabaseLibraryDeployer.DeployAsync"/> did.</summary>
public enum LibraryDeployStatus
{
    /// <summary>The program does not bind the database engine library; nothing was looked up or written.</summary>
    NotNeeded,

    /// <summary>The same file is already beside the program.</summary>
    UpToDate,

    /// <summary>The file was written beside the program.</summary>
    Copied,

    /// <summary>The program needs the library, but the toolchain search did not find it.</summary>
    Missing,

    /// <summary>The copy failed; <see cref="LibraryDeployResult.Detail"/> is the reason.</summary>
    Failed,
}

/// <param name="Detail">The reason of a <see cref="LibraryDeployStatus.Failed"/> copy; null otherwise.</param>
public sealed record LibraryDeployResult(LibraryDeployStatus Status, string? Detail = null);

/// <summary>
/// I-225: after a successful build, puts the database engine library beside the program when the program needs it. The
/// decision comes from the resolved unit list of the project: the unit that loads the library is in the build. The file
/// is written only when it is missing or differs from the toolchain's. I-280: for every project a request built, not
/// only the loaded one; <paramref name="projectUnitNames"/> gives the units of a project file in a configuration.
/// </summary>
public sealed class DatabaseLibraryDeployer(IFileSystem fileSystem, IToolchainLocator toolchain,
    Func<string, string, CancellationToken, Task<ImmutableArray<string>>> projectUnitNames)
{
    /// <summary>The file name of the database engine library, as the data-access units load it.</summary>
    public const string FileName = "sqlite3.dll";

    /// <summary>The data-access unit that loads <see cref="FileName"/>; every unit that needs the library uses it.</summary>
    public const string BindingUnit = "SQLite3API";

    /// <param name="built">A project the request built: its file, configuration and program (<c>OutputPath</c>).</param>
    public async Task<LibraryDeployResult> DeployAsync(FreexComp.Build.ProjectBuildResult built)
    {
        ArgumentNullException.ThrowIfNull(built);
        if (built.OutputPath is not { } outputPath)
            return new LibraryDeployResult(LibraryDeployStatus.NotNeeded);
        var units = await projectUnitNames(built.ProjectPath, built.Configuration, CancellationToken.None);
        if (!units.Contains(BindingUnit, StringComparer.OrdinalIgnoreCase))
            return new LibraryDeployResult(LibraryDeployStatus.NotNeeded);
        if (toolchain.Resolve().DatabaseLibraryDir is not { } directory)
            return new LibraryDeployResult(LibraryDeployStatus.Missing);

        var source = Path.Combine(directory, FileName);
        var target = Path.Combine(Path.GetDirectoryName(outputPath) ?? "", FileName);
        try
        {
            var bytes = fileSystem.ReadAllBytes(source);
            if (fileSystem.FileExists(target) && fileSystem.GetLength(target) == bytes.LongLength
                && fileSystem.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
            {
                return new LibraryDeployResult(LibraryDeployStatus.UpToDate);
            }

            fileSystem.WriteAllBytes(target, bytes);
            return new LibraryDeployResult(LibraryDeployStatus.Copied);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new LibraryDeployResult(LibraryDeployStatus.Failed, e.Message);
        }
    }
}
