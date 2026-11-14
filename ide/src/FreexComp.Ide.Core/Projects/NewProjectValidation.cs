using System.Globalization;
using FreexComp.Compiler.Syntax;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Projects;

/// <summary>What the New Project dialog returns (UI-DLG-031).</summary>
public sealed record NewProjectRequest(string TemplateId, string Name, string Location);

/// <summary>Result of <see cref="IProjectService.CreateProjectAsync"/>.</summary>
public enum NewProjectOutcome
{
    /// <summary>Created and opened.</summary>
    Created,

    /// <summary>The unsaved-changes prompt of the current project was canceled; nothing changed.</summary>
    Canceled,

    /// <summary><c>Msg.Project.CreateFailed</c> was shown; everything created was removed.</summary>
    Failed,
}

/// <summary>Name and location rules of a new project (IDE-PM-021/022, dialogs.md §3), first failure wins.</summary>
public static class NewProjectValidation
{
    public const int MaxNameLength = 64;

    public const int MaxPathLength = 240;

    /// <summary>Characters never valid in a folder name segment (besides control characters).</summary>
    private static readonly char[] InvalidSegmentChars = ['<', '>', ':', '"', '|', '?', '*'];

    /// <summary>
    /// Empty → <c>Msg.Project.NameEmpty</c>; not <c>^[A-Za-z_][A-Za-z0-9_]*$</c> → <c>NameInvalid</c>; longer than 64 →
    /// <c>NameTooLong</c> (64); a reserved word of the language or <c>System</c> → <c>NameReserved</c> (the name).
    /// </summary>
    public static OperationResult ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0)
            return OperationResult.Fail("Msg.Project.NameEmpty");
        if (!IsIdentifier(name))
            return OperationResult.Fail("Msg.Project.NameInvalid");
        if (name.Length > MaxNameLength)
            return OperationResult.Fail("Msg.Project.NameTooLong", MaxNameLength.ToString(CultureInfo.CurrentCulture));
        if (SyntaxFacts.IsReservedWord(name) || string.Equals(name, "System", StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail("Msg.Project.NameReserved", name);
        return OperationResult.Ok;
    }

    /// <summary>
    /// Empty → <c>Msg.Project.LocationEmpty</c>; not an absolute, syntactically valid path → <c>LocationInvalid</c>;
    /// <c>&lt;location&gt;\&lt;name&gt;</c> exists with any entry → <c>LocationNotEmpty</c> (that folder); the longest file
    /// the template creates longer than 240 → <c>PathTooLong</c> (240). Assumes a valid <paramref name="name"/>.
    /// </summary>
    public static OperationResult ValidateLocation(string location, string name, ProjectTemplate template, IdeFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(fileSystem);
        var syntax = ValidateLocationSyntax(location);
        if (!syntax.Success)
            return syntax;

        var target = Path.Combine(NormalizeLocation(location)!, name);
        if (fileSystem.DirectoryExists(target)
            && (fileSystem.EnumerateFiles(target, "*").Count > 0 || fileSystem.EnumerateDirectories(target).Count > 0))
            return OperationResult.Fail("Msg.Project.LocationNotEmpty", target);

        var longest = template.Files.Select(f => Path.Combine(target, ProjectTemplate.Expand(f.Target, name)).Length).DefaultIfEmpty(target.Length).Max();
        if (longest > MaxPathLength)
            return OperationResult.Fail("Msg.Project.PathTooLong", MaxPathLength.ToString(CultureInfo.CurrentCulture));
        return OperationResult.Ok;
    }

    /// <summary>The first two location rules only (no name needed): <c>LocationEmpty</c>, <c>LocationInvalid</c>.</summary>
    public static OperationResult ValidateLocationSyntax(string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (string.IsNullOrWhiteSpace(location))
            return OperationResult.Fail("Msg.Project.LocationEmpty");
        return NormalizeLocation(location) is null ? OperationResult.Fail("Msg.Project.LocationInvalid") : OperationResult.Ok;
    }

    /// <summary>The location as a full path with <c>\</c> (UI-DLG-011), or null when it is not a valid absolute path.</summary>
    public static string? NormalizeLocation(string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var text = location.Trim().Replace('/', '\\');
        if (!Path.IsPathFullyQualified(text))
            return null;

        var root = Path.GetPathRoot(text) ?? "";
        // A UNC root needs server and share (\\server\share).
        if (text.StartsWith(@"\\", StringComparison.Ordinal) && root.TrimEnd('\\').Count(c => c == '\\') < 3)
            return null;
        if (text[root.Length..].Split('\\').Any(s => s.IndexOfAny(InvalidSegmentChars) >= 0 || s.Any(char.IsControl)))
            return null;

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(text));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>The text of an inline message result (resource <c>Msg_…</c> with its arguments); empty on success.</summary>
    public static string MessageText(OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.MessageId is not { } id)
            return "";
        var format = Strings.ResourceManager.GetString(id.Replace('.', '_'), CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"No resource for {id}.");
        return string.Format(CultureInfo.CurrentCulture, format, [.. result.Args]);
    }

    private static bool IsIdentifier(string name) =>
        (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
