using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Toolchain;

/// <summary>
/// CMP-628/629 (C-167), CMP-1100…1106 (C-295): the effective application manifest of a link. Embedding is variant 1 of CMP-629
/// (<c>lld-link /manifest:embed /manifestinput:</c>), verified on the pinned toolchain: its built-in manifest merger needs no
/// external <c>mt.exe</c>.
/// </summary>
internal static class ApplicationManifest
{
    /// <summary>
    /// CMP-1101…1105: the generated manifest (UTF-8 without BOM, LF, final LF) — a pure function of the application type, the
    /// execution level and the DPI awareness. The Common Controls 6 dependency is emitted for GUI programs only; the two DPI lines
    /// are omitted when the awareness is not declared (the default of a console program).
    /// </summary>
    public static string Generate(AppType appType, ManifestExecutionLevel executionLevel = ManifestExecutionLevel.AsInvoker,
        ManifestDpiAwareness dpiAwareness = ManifestDpiAwareness.Default)
    {
        var gui = appType == AppType.Gui;
        var dpi = dpiAwareness switch
        {
            ManifestDpiAwareness.PerMonitorV2 => ("true/pm", "PerMonitorV2"),
            ManifestDpiAwareness.System => ("true", "System"),
            ManifestDpiAwareness.Unaware => ("false", "Unaware"),
            _ => gui ? ("true/pm", "PerMonitorV2") : ((string, string)?)null,
        };
        var level = LevelWord(executionLevel);
        var text = new StringBuilder();
        text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        text.Append("<assembly xmlns=\"urn:schemas-microsoft-com:asm.v1\" manifestVersion=\"1.0\">\n");
        if (gui)
        {
            text.Append("  <dependency>\n");
            text.Append("    <dependentAssembly>\n");
            text.Append("      <assemblyIdentity type=\"win32\" name=\"Microsoft.Windows.Common-Controls\" version=\"6.0.0.0\"\n");
            text.Append("                        processorArchitecture=\"amd64\" publicKeyToken=\"6595b64144ccf1df\" language=\"*\"/>\n");
            text.Append("    </dependentAssembly>\n");
            text.Append("  </dependency>\n");
        }

        text.Append("  <application xmlns=\"urn:schemas-microsoft-com:asm.v3\">\n");
        text.Append("    <windowsSettings>\n");
        if (dpi is var (old, current))
        {
            text.Append("      <dpiAware xmlns=\"http://schemas.microsoft.com/SMI/2005/WindowsSettings\">").Append(old).Append("</dpiAware>\n");
            text.Append("      <dpiAwareness xmlns=\"http://schemas.microsoft.com/SMI/2016/WindowsSettings\">").Append(current).Append("</dpiAwareness>\n");
        }

        text.Append("      <longPathAware xmlns=\"http://schemas.microsoft.com/SMI/2016/WindowsSettings\">true</longPathAware>\n");
        text.Append("    </windowsSettings>\n");
        text.Append("  </application>\n");
        text.Append("  <compatibility xmlns=\"urn:schemas-microsoft-com:compatibility.v1\">\n");
        text.Append("    <application>\n");
        text.Append("      <supportedOS Id=\"{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}\"/>\n");
        text.Append("    </application>\n");
        text.Append("  </compatibility>\n");
        text.Append("  <trustInfo xmlns=\"urn:schemas-microsoft-com:asm.v3\">\n");
        text.Append("    <security>\n");
        text.Append("      <requestedPrivileges>\n");
        text.Append("        <requestedExecutionLevel level=\"").Append(level).Append("\" uiAccess=\"false\"/>\n");
        text.Append("      </requestedPrivileges>\n");
        text.Append("    </security>\n");
        text.Append("  </trustInfo>\n");
        text.Append("</assembly>\n");
        return text.ToString();
    }

    /// <summary>CMP-1104: the execution level as it is spelled in the manifest and in every interface.</summary>
    public static string LevelWord(ManifestExecutionLevel level) => level switch
    {
        ManifestExecutionLevel.HighestAvailable => "highestAvailable",
        ManifestExecutionLevel.RequireAdministrator => "requireAdministrator",
        _ => "asInvoker",
    };

    /// <summary>
    /// The manifest to pass to lld-link, or null for none. <c>options.Manifest</c> is <c>default</c>, <c>none</c>, an absolute
    /// path, or "" (CMP-1100: <c>default</c> for console and GUI programs). <c>default</c> writes the generated text to
    /// <c>&lt;objdir&gt;pp.manifest</c>; a path is returned as is. A library (CMP-1060) has none by default and treats
    /// <c>default</c> as <c>none</c>: the generated manifest describes an application, not a DLL.
    /// <paramref name="generated"/>: the returned file is the generated one (the settings of CMP-1104/1105 apply to it only).
    /// </summary>
    public static string? Prepare(CompilationOptions options, string objectDirectory, IFileSystem fs) =>
        Prepare(options, objectDirectory, fs, out _);

    public static string? Prepare(CompilationOptions options, string objectDirectory, IFileSystem fs, out bool generated)
    {
        generated = false;
        var effective = options.Manifest == "" ? "default" : options.Manifest;
        if (options.AppType == AppType.Library && effective == "default")
        {
            effective = "none";
        }

        switch (effective)
        {
            case "none":
                return null;
            case "default":
                var path = Path.Combine(objectDirectory, "app.manifest");
                fs.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(
                    Generate(options.AppType, options.ManifestExecutionLevel, options.ManifestDpiAwareness)));
                generated = true;
                return path;
            default:
                return PathUtil.Normalize(effective);
        }
    }
}
