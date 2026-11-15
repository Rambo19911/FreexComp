using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>
/// C-167 (toolchain.md CMP-628, CMP-629 variant 1): a real link with the pinned <c>lld-link</c>, the exe's <c>RT_MANIFEST</c>
/// resource 1 read back by walking the PE resource directory. <c>lld-link /manifestinput</c> runs its built-in manifest merger,
/// which re-serialises the XML (it drops <c>standalone</c> and puts its own <c>trustInfo</c> first), so the resource is compared
/// with <c>app.manifest</c> as XML: the same elements, attributes and text, in any sibling order.
/// </summary>
[Trait("Phase", "4")]
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class ManifestEmbeddedTests : IDisposable
{
    private const string Source = "program P;\nbegin\nend.\n";

    private readonly string _dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));

    public ManifestEmbeddedTests()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        PhysicalFileSystem.Instance.WriteAllBytes(Path.Combine(_dir, "P.pas"), Encoding.UTF8.GetBytes(Source));
    }

    private string Obj => Path.Combine(_dir, "obj");

    private string Exe => Path.Combine(_dir, "P.exe");

    [Fact]
    [Trait("Req", "CMP-628")]
    [Trait("Req", "CMP-629")]
    public void GuiProgram_EmbedsAppManifestAsResource1()
    {
        Build(AppType.Gui, "");

        var manifest = Path.Combine(Obj, "app.manifest");
        Assert.Equal(new UTF8Encoding(false).GetBytes(ApplicationManifest.DefaultText), File.ReadAllBytes(manifest));
        AssertSameXml(File.ReadAllText(manifest), ManifestResource(Exe));
        Assert.Contains("/manifestinput:" + manifest, File.ReadAllLines(Path.Combine(Obj, "link.rsp")));
    }

    [Fact]
    [Trait("Req", "CMP-628")]
    public void ConsoleProgram_HasNoManifest()
    {
        Build(AppType.Console, "");

        Assert.Null(ManifestResource(Exe));
        Assert.False(File.Exists(Path.Combine(Obj, "app.manifest")));
    }

    [Fact]
    [Trait("Req", "CMP-628")]
    public void ManifestNone_GuiProgramHasNoManifest()
    {
        Build(AppType.Gui, "none");

        Assert.Null(ManifestResource(Exe));
    }

    /// <summary>A user file goes straight to lld-link; editing only its content relinks (CMP-629: part of the CMP-155 check).</summary>
    [Fact]
    [Trait("Req", "CMP-629")]
    [Trait("Req", "CMP-155")]
    public void UserManifest_IsEmbedded_AndAContentEditRelinks()
    {
        var user = Path.Combine(_dir, "res", "my.manifest");
        PhysicalFileSystem.Instance.WriteAllBytes(user, Encoding.UTF8.GetBytes(UserManifest("1.0.0.0")));
        Build(AppType.Gui, user);
        AssertSameXml(UserManifest("1.0.0.0"), ManifestResource(Exe));
        Assert.Contains("/manifestinput:" + user, File.ReadAllLines(Path.Combine(Obj, "link.rsp")));

        Assert.DoesNotContain(Build(AppType.Gui, user), c => c.Contains("lld-link", StringComparison.OrdinalIgnoreCase));

        PhysicalFileSystem.Instance.WriteAllBytes(user, Encoding.UTF8.GetBytes(UserManifest("2.0.0.0")));
        Assert.Contains(Build(AppType.Gui, user), c => c.Contains("lld-link", StringComparison.OrdinalIgnoreCase));
        AssertSameXml(UserManifest("2.0.0.0"), ManifestResource(Exe));
    }

    public void Dispose()
    {
        try
        {
            TestDirectories.Delete(_dir);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string UserManifest(string version) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
          <assemblyIdentity type="win32" name="FreexComp.Test.P" version="{version}"/>
          <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
            <security>
              <requestedPrivileges>
                <requestedExecutionLevel level="asInvoker" uiAccess="false"/>
              </requestedPrivileges>
            </security>
          </trustInfo>
        </assembly>

        """;

    private List<string> Build(AppType appType, string manifest)
    {
        var commands = new List<string>();
        var options = new CompilationOptions
        {
            MainFile = Path.Combine(_dir, "P.pas"),
            RtlDirectory = TestRtl.Directory,
            ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
            ObjectDirectory = Obj,
            AppType = appType,
            Manifest = manifest,
        };

        var result = Compilation.Create(options, [], PhysicalFileSystem.Instance)
            .Emit(new EmitOptions { OutputPath = Exe, CommandLineSink = commands.Add }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "W4003");
        return commands;
    }

    private static void AssertSameXml(string expected, string? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(Canonical(XDocument.Parse(expected).Root!), Canonical(XDocument.Parse(actual).Root!));
    }

    /// <summary>Element name, sorted attributes, trimmed text and the sorted canonical forms of its children.</summary>
    private static string Canonical(XElement e) =>
        "<" + e.Name + string.Concat(e.Attributes().Select(a => $" {a.Name}=\"{a.Value}\"").Order(StringComparer.Ordinal)) + ">"
        + string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value.Trim()))
        + string.Concat(e.Elements().Select(Canonical).Order(StringComparer.Ordinal))
        + "</" + e.Name + ">";

    /// <summary>The UTF-8 text of resource type 24 (<c>RT_MANIFEST</c>) name 1, first language; null when the exe has none.</summary>
    private static string? ManifestResource(string exe)
    {
        var pe = File.ReadAllBytes(exe);
        var header = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(0x3C));
        Assert.Equal(0x00004550u, BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(header)));
        var sections = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(header + 6));
        var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(header + 20));
        var optional = header + 24;
        Assert.Equal(0x20B, BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(optional)));        // PE32+
        var resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(optional + 112 + (2 * 8)));
        if (resourceRva == 0)
        {
            return null;
        }

        var table = optional + optionalSize;
        int Offset(uint rva)
        {
            for (var i = 0; i < sections; i++)
            {
                var s = table + (i * 40);
                var size = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 8));
                var address = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 12));
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 20));
                if (rva >= address && rva < address + size)
                {
                    return (int)(rva - address + raw);
                }
            }

            throw new InvalidOperationException($"RVA 0x{rva:X} is in no section");
        }

        var root = Offset(resourceRva);
        uint? Child(uint directory, uint? id)
        {
            var at = root + (int)directory;
            var named = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(at + 12));
            var ids = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(at + 14));
            for (var i = 0; i < named + ids; i++)
            {
                var entry = at + 16 + (i * 8);
                var name = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(entry));
                if (id is null || name == id)
                {
                    return BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(entry + 4));
                }
            }

            return null;
        }

        if (Child(0, 24) is not { } type || Child(type & 0x7FFFFFFF, 1) is not { } named1 || Child(named1 & 0x7FFFFFFF, null) is not { } language)
        {
            return null;
        }

        Assert.Equal(0u, language & 0x80000000);                                                     // a data entry, not a directory
        var data = root + (int)language;
        var dataRva = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(data));
        var dataSize = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(data + 4));
        return Encoding.UTF8.GetString(pe, Offset(dataRva), dataSize).TrimStart('﻿');
    }
}
