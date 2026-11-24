using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Toolchain;

/// <summary>
/// CMP-628/629 (C-167): the effective application manifest of a link. Embedding is variant 1 of CMP-629
/// (<c>lld-link /manifest:embed /manifestinput:</c>), verified on the pinned toolchain: its built-in manifest merger needs no
/// external <c>mt.exe</c>.
/// </summary>
internal static class ApplicationManifest
{
    /// <summary>The CMP-628 default manifest (UTF-8 without BOM, LF).</summary>
    public const string DefaultText =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
        + "<assembly xmlns=\"urn:schemas-microsoft-com:asm.v1\" manifestVersion=\"1.0\">\n"
        + "  <dependency>\n"
        + "    <dependentAssembly>\n"
        + "      <assemblyIdentity type=\"win32\" name=\"Microsoft.Windows.Common-Controls\" version=\"6.0.0.0\"\n"
        + "                        processorArchitecture=\"amd64\" publicKeyToken=\"6595b64144ccf1df\" language=\"*\"/>\n"
        + "    </dependentAssembly>\n"
        + "  </dependency>\n"
        + "  <application xmlns=\"urn:schemas-microsoft-com:asm.v3\">\n"
        + "    <windowsSettings>\n"
        + "      <dpiAware xmlns=\"http://schemas.microsoft.com/SMI/2005/WindowsSettings\">true/pm</dpiAware>\n"
        + "      <dpiAwareness xmlns=\"http://schemas.microsoft.com/SMI/2016/WindowsSettings\">PerMonitorV2</dpiAwareness>\n"
        + "    </windowsSettings>\n"
        + "  </application>\n"
        + "  <compatibility xmlns=\"urn:schemas-microsoft-com:compatibility.v1\">\n"
        + "    <application>\n"
        + "      <supportedOS Id=\"{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}\"/>\n"
        + "    </application>\n"
        + "  </compatibility>\n"
        + "  <trustInfo xmlns=\"urn:schemas-microsoft-com:asm.v3\">\n"
        + "    <security>\n"
        + "      <requestedPrivileges>\n"
        + "        <requestedExecutionLevel level=\"asInvoker\" uiAccess=\"false\"/>\n"
        + "      </requestedPrivileges>\n"
        + "    </security>\n"
        + "  </trustInfo>\n"
        + "</assembly>\n";

    /// <summary>
    /// The manifest to pass to lld-link, or null for none. <paramref name="manifest"/> is <c>default</c>, <c>none</c>, an absolute
    /// path, or "" (<c>default</c> for GUI programs, <c>none</c> for console programs). <c>default</c> writes
    /// <c>&lt;objdir&gt;\app.manifest</c>; a path is returned as is. A library (CMP-1060) has none by default and treats
    /// <c>default</c> as <c>none</c>: the default manifest describes an application, not a DLL.
    /// </summary>
    public static string? Prepare(string manifest, AppType appType, string objectDirectory, IFileSystem fs)
    {
        var effective = manifest == "" ? (appType == AppType.Gui ? "default" : "none") : manifest;
        if (appType == AppType.Library && effective == "default")
        {
            effective = "none";
        }

        switch (effective)
        {
            case "none":
                return null;
            case "default":
                var path = Path.Combine(objectDirectory, "app.manifest");
                fs.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(DefaultText));
                return path;
            default:
                return PathUtil.Normalize(effective);
        }
    }
}
