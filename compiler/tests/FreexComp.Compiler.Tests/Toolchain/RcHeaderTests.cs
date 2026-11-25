using System.Globalization;
using System.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>resource-compiler.md §8 (CMP-1328): the product's own header files for resource scripts.</summary>
[Trait("Phase", "8")]
[Trait("Req", "CMP-1328")]
public sealed class RcHeaderTests
{
    /// <summary>
    /// The table a second time, written from the reference pages (not copied from the header): "Resource Types", the
    /// VS_FIXEDFILEINFO structure page, the VERSIONINFO resource page (the version resource identifier is 1; the file flags mask
    /// is the value the page gives, 0x3f) and the page on manifests as a resource (identifiers 1 and 2).
    /// </summary>
    private static readonly Dictionary<string, long> Reference = new(StringComparer.Ordinal)
    {
        // Resource Types
        ["RT_ACCELERATOR"] = 9, ["RT_ANICURSOR"] = 21, ["RT_ANIICON"] = 22, ["RT_BITMAP"] = 2, ["RT_CURSOR"] = 1, ["RT_DIALOG"] = 5,
        ["RT_DLGINCLUDE"] = 17, ["RT_FONT"] = 8, ["RT_FONTDIR"] = 7, ["RT_GROUP_CURSOR"] = 1 + 11, ["RT_GROUP_ICON"] = 3 + 11,
        ["RT_HTML"] = 23, ["RT_ICON"] = 3, ["RT_MANIFEST"] = 24, ["RT_MENU"] = 4, ["RT_MESSAGETABLE"] = 11, ["RT_PLUGPLAY"] = 19,
        ["RT_RCDATA"] = 10, ["RT_STRING"] = 6, ["RT_VERSION"] = 16, ["RT_VXD"] = 20,

        // manifest resource identifiers
        ["CREATEPROCESS_MANIFEST_RESOURCE_ID"] = 1, ["ISOLATIONAWARE_MANIFEST_RESOURCE_ID"] = 2,

        // VERSIONINFO resource
        ["VS_VERSION_INFO"] = 1, ["VS_FFI_FILEFLAGSMASK"] = 0x3f,

        // VS_FIXEDFILEINFO: dwFileFlags
        ["VS_FF_DEBUG"] = 0x00000001, ["VS_FF_INFOINFERRED"] = 0x00000010, ["VS_FF_PATCHED"] = 0x00000004, ["VS_FF_PRERELEASE"] = 0x00000002,
        ["VS_FF_PRIVATEBUILD"] = 0x00000008, ["VS_FF_SPECIALBUILD"] = 0x00000020,

        // dwFileOS
        ["VOS_DOS"] = 0x00010000, ["VOS_NT"] = 0x00040000, ["VOS__WINDOWS16"] = 0x00000001, ["VOS__WINDOWS32"] = 0x00000004,
        ["VOS_OS216"] = 0x00020000, ["VOS_OS232"] = 0x00030000, ["VOS__PM16"] = 0x00000002, ["VOS__PM32"] = 0x00000003, ["VOS_UNKNOWN"] = 0x00000000,
        ["VOS_DOS_WINDOWS16"] = 0x00010001, ["VOS_DOS_WINDOWS32"] = 0x00010004, ["VOS_NT_WINDOWS32"] = 0x00040004,
        ["VOS_OS216_PM16"] = 0x00020002, ["VOS_OS232_PM32"] = 0x00030003,

        // dwFileType
        ["VFT_APP"] = 0x00000001, ["VFT_DLL"] = 0x00000002, ["VFT_DRV"] = 0x00000003, ["VFT_FONT"] = 0x00000004, ["VFT_STATIC_LIB"] = 0x00000007,
        ["VFT_UNKNOWN"] = 0x00000000, ["VFT_VXD"] = 0x00000005,

        // dwFileSubtype
        ["VFT2_DRV_COMM"] = 0x0000000A, ["VFT2_DRV_DISPLAY"] = 0x00000004, ["VFT2_DRV_INSTALLABLE"] = 0x00000008, ["VFT2_DRV_KEYBOARD"] = 0x00000002,
        ["VFT2_DRV_LANGUAGE"] = 0x00000003, ["VFT2_DRV_MOUSE"] = 0x00000005, ["VFT2_DRV_NETWORK"] = 0x00000006, ["VFT2_DRV_PRINTER"] = 0x00000001,
        ["VFT2_DRV_SOUND"] = 0x00000009, ["VFT2_DRV_SYSTEM"] = 0x00000007, ["VFT2_DRV_VERSIONED_PRINTER"] = 0x0000000C, ["VFT2_UNKNOWN"] = 0x00000000,
        ["VFT2_FONT_RASTER"] = 0x00000001, ["VFT2_FONT_TRUETYPE"] = 0x00000003, ["VFT2_FONT_VECTOR"] = 0x00000002,
    };

    private static string Text(string name) => Encoding.UTF8.GetString(ResourceHeaders.Files.Single(f => f.Name == name).Content);

    [Fact]
    public void ConstantsHaveTheReferenceValues()
    {
        var lines = Text("rcdefs.h").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("/* FreexComp ", lines[0], StringComparison.Ordinal);                 // one comment line naming the product
        Assert.EndsWith("*/", lines[0], StringComparison.Ordinal);

        var defined = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in lines[1..])
        {
            var parts = line.Split(' ');
            Assert.True(parts.Length == 3 && parts[0] == "#define", "only #define lines: " + line);
            var value = parts[2].TrimEnd('L');
            defined.Add(parts[1], value.StartsWith("0x", StringComparison.Ordinal)
                ? long.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : long.Parse(value, CultureInfo.InvariantCulture));
        }

        Assert.Equal(Reference.OrderBy(p => p.Key, StringComparer.Ordinal), defined.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void CustomaryIncludeNamesResolve()
    {
        string[] customary = ["windows.h", "winnt.h", "winres.h", "winresrc.h", "winuser.h", "winver.h"];

        Assert.Equal([.. customary.Append("rcdefs.h").Order(StringComparer.Ordinal)], ResourceHeaders.Files.Select(f => f.Name));
        foreach (var name in customary)
        {
            Assert.Equal("#include \"rcdefs.h\"\n", Text(name));
        }
    }
}
