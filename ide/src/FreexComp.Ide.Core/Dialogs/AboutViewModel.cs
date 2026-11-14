using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>What closed the About dialog.</summary>
public enum AboutResult
{
    Closed,

    /// <summary><c>Third-Party Licenses...</c>: the caller runs <c>help.licenses</c> once About has closed.</summary>
    ShowLicenses,
}

/// <summary>
/// The About dialog (dialogs.md section 15, UI-DLG-150): product name, version from the assembly attributes, the license lines,
/// runtime, toolchain and settings directory, all read-only text; <c>Copy Info</c> copies them as <c>Label: value</c>
/// lines and keeps the dialog open; <c>Third-Party Licenses...</c> closes it with <see cref="AboutResult.ShowLicenses"/>.
/// No online links, no update check.
/// </summary>
public sealed class AboutViewModel : DialogViewModel<AboutResult>
{
    public const string DialogName = "About";

    private readonly IClipboardService _clipboard;

    /// <param name="toolchain">The located toolchain as <c>LLVM &lt;version&gt; - &lt;bin dir&gt;</c>; null = not found.</param>
    public AboutViewModel(IUserPaths paths, IClipboardService clipboard, string? toolchain = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _clipboard = clipboard;
        Version = FormatVersion(typeof(AboutViewModel).Assembly);
        Runtime = string.Create(CultureInfo.InvariantCulture,
            $".NET {Environment.Version}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}");
        Toolchain = toolchain ?? Strings.Msg_About_ToolchainNotFound;
        Settings = paths.RoamingDirectory;
        Buttons =
        [
            new DialogButtonViewModel("CopyInfo", Strings.About_CopyInfo, CopyInfoAsync) { IsAuxiliary = true, AccessLabel = "_" + Strings.About_CopyInfo },
            new DialogButtonViewModel("Licenses", Strings.About_Licenses, () => Close(AboutResult.ShowLicenses))
            {
                IsAuxiliary = true,
                AccessLabel = "_" + Strings.About_Licenses,
            },
            new DialogButtonViewModel("Close", Strings.Button_Close, () => Close(AboutResult.Closed)) { IsDefault = true, IsCancel = true, IsAccent = true },
        ];
    }

    public override string Name => DialogName;

    public override string Title => Strings.About_Title;

    public override double Width => 520;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public string ProductName => Strings.App_ProductName;

    public string Version { get; }

    public string License => Strings.Msg_About_License;

    public string Runtime { get; }

    public string Toolchain { get; }

    public string Settings { get; }

    /// <summary>The Copy Info text: one <c>Label: value</c> line per field.</summary>
    public string InfoText => string.Join(Environment.NewLine,
        $"{Strings.About_Label_Product}: {ProductName}",
        $"{Strings.About_Label_Version}: {Version[(Strings.About_Version.Length + 1)..]}",
        $"{Strings.About_Label_License}: {License.ReplaceLineEndings(" ")}",
        $"{Strings.About_Label_Runtime}: {Runtime}",
        $"{Strings.About_Label_Toolchain}: {Toolchain}",
        $"{Strings.About_Label_Settings}: {Settings}");

    /// <summary>
    /// <c>Version &lt;informational version&gt; (build &lt;date&gt;, &lt;short commit&gt;)</c>; the build part comes from the
    /// <c>AssemblyMetadata</c> keys <c>BuildDate</c> and <c>Commit</c> and is omitted when they are absent.
    /// </summary>
    public static string FormatVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "";
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);
        var commit = metadata.GetValueOrDefault("Commit");
        var build = new[] { metadata.GetValueOrDefault("BuildDate"), commit is { Length: > 7 } ? commit[..7] : commit }
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();
        var text = Strings.About_Version + " " + informational;
        return build.Count == 0 ? text : text + " (" + Strings.About_Build + " " + string.Join(", ", build) + ")";
    }

    /// <summary>Registers <c>help.about</c> and <c>help.licenses</c> (commands.md, owner IHelpService).</summary>
    public static void RegisterCommands(ICommandService commands, IDialogService dialogs, IUserPaths paths, IClipboardService clipboard, ILayoutService layout)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("help.about", new Handler(async () =>
        {
            if (await dialogs.ShowAsync(new AboutViewModel(paths, clipboard)) == AboutResult.ShowLicenses)
                await commands.ExecuteAsync("help.licenses");
        }));
        commands.RegisterHandler("help.licenses", new Handler(() => dialogs.ShowAsync(new LicensesViewModel(ThirdPartyNotices.LoadEmbedded(), layout))));
    }

    private Task CopyInfoAsync() => _clipboard.SetTextAsync(InfoText);

    private sealed class Handler(Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument) => execute();
    }
}
