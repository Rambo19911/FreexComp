using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FreexComp.Setup.Tests;

[Trait("Phase", "8")]
public sealed class ManifestTests
{
    internal static readonly Dictionary<string, string> SmallPayload = new()
    {
        ["FreexComp.Ide.exe"] = "fake IDE",
        ["opc.exe"] = "fake compiler",
        [@"licenses\LICENSE"] = "licence text",
        [@"rtl\src\System.pas"] = "unit System; interface implementation end.",
        [@"third_party\llvm\bin\clang.exe"] = "fake clang",
    };

    [Fact]
    [Trait("Req", "INS-001")]
    [Trait("Req", "INS-003")]
    [Trait("Req", "INS-004")]
    public void VerifiesGoodPayload()
    {
        using var box = new SetupSandbox();
        var payload = box.Payload("good", SmallPayload);
        var program = box.FakeProgram();
        var package = box.Package(payload, program);

        // the manifest: one "<sha256>  <path with \>" line per file except itself, sorted ordinal-ignore-case, LF
        var manifest = File.ReadAllText(Path.Combine(payload, PackageFormat.ManifestName));
        var expected = string.Concat(SmallPayload.Keys.Order(StringComparer.OrdinalIgnoreCase)
            .Select(p => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SmallPayload[p]))) + "  " + p + "\n"));
        Assert.Equal(expected, manifest);

        // the trailer: magic, zip offset (= program length), SHA-256 of the manifest bytes
        var bytes = File.ReadAllBytes(package);
        var trailer = bytes.AsSpan(bytes.Length - 48);
        Assert.Equal("FXCSETUP"u8.ToArray(), trailer[..8].ToArray());
        Assert.Equal(new FileInfo(program).Length, BinaryPrimitives.ReadInt64LittleEndian(trailer[8..16]));
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(Path.Combine(payload, PackageFormat.ManifestName))), trailer[16..].ToArray());
        Assert.Equal(File.ReadAllBytes(program), bytes[..(int)new FileInfo(program).Length]);

        // /verify accepts it, writes nothing
        var result = box.Run(package, ["/S", "/verify"]);
        Assert.Equal(ExitCode.Ok, result.Code);
        Assert.False(Directory.Exists(box.Home));

        // same inputs, byte-identical package (sorted entries, fixed time stamps)
        File.SetLastWriteTimeUtc(Path.Combine(payload, "opc.exe"), DateTime.UtcNow.AddDays(-3));
        var again = box.Package(payload, program, "again.exe");
        Assert.Equal(bytes, File.ReadAllBytes(again));

        // and it installs: every payload file and the manifest arrive unchanged
        Assert.Equal(ExitCode.Ok, box.Run(package, box.Switches("/noshortcuts", "/noassoc")).Code);
        foreach (var (path, content) in SmallPayload)
            Assert.Equal(content, File.ReadAllText(Path.Combine(box.Home, path)));
        Assert.Equal(manifest, File.ReadAllText(Path.Combine(box.Home, PackageFormat.ManifestName)));
    }

    [Fact]
    [Trait("Req", "INS-003")]
    public void FlippedByteFailsBeforeAnyWrite()
    {
        using var box = new SetupSandbox();

        // one byte of one payload file differs from its manifest line
        var payload = box.Payload("flipped", SmallPayload);
        PackageWriter.WriteManifest(payload);
        var licence = Path.Combine(payload, "licenses", "LICENSE");
        var content = File.ReadAllBytes(licence);
        content[3] ^= 0x01;
        File.WriteAllBytes(licence, content);
        var package = box.PackageAsIs(payload);

        var result = box.Run(package, box.Switches());
        Assert.Equal(ExitCode.DamagedPackage, result.Code);
        Assert.Equal(@"The installation package is damaged (licenses\LICENSE).", result.Message);
        AssertNothingWritten(box);
        Assert.Equal(ExitCode.DamagedPackage, box.Run(package, ["/S", "/verify"]).Code);

        // one byte of the package itself flipped inside the compressed data of an entry
        var good = box.Package(box.Payload("good", SmallPayload), name: "good.exe");
        var bytes = File.ReadAllBytes(good);
        var offset = (int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(bytes.Length - 40, 8));
        var header = bytes.AsSpan(offset).IndexOf("licenses/LICENSE"u8);   // the name in the entry's local header
        Assert.True(header > 0);
        var entry = offset + header + "licenses/LICENSE".Length + 2;        // no extra field: the data follows the name
        bytes[entry] ^= 0x01;
        var broken = box.In("broken.exe");
        File.WriteAllBytes(broken, bytes);

        result = box.Run(broken, box.Switches());
        Assert.Equal(ExitCode.DamagedPackage, result.Code);
        Assert.StartsWith("The installation package is damaged (", result.Message, StringComparison.Ordinal);
        AssertNothingWritten(box);
    }

    [Fact]
    [Trait("Req", "INS-003")]
    public void ExtraAndMissingEntriesFail()
    {
        using var box = new SetupSandbox();

        var extra = box.Payload("extra", SmallPayload);
        PackageWriter.WriteManifest(extra);
        File.WriteAllText(Path.Combine(extra, "rtl", "src", "Planted.pas"), "unit Planted;");
        var result = box.Run(box.PackageAsIs(extra, name: "extra.exe"), box.Switches());
        Assert.Equal(new SetupResult(ExitCode.DamagedPackage, @"The installation package is damaged (rtl\src\Planted.pas)."), result);
        AssertNothingWritten(box);

        var missing = box.Payload("missing", SmallPayload);
        PackageWriter.WriteManifest(missing);
        File.Delete(Path.Combine(missing, "opc.exe"));
        result = box.Run(box.PackageAsIs(missing, name: "missing.exe"), box.Switches());
        Assert.Equal(new SetupResult(ExitCode.DamagedPackage, "The installation package is damaged (opc.exe)."), result);
        AssertNothingWritten(box);

        // a manifest that does not match the trailer hash
        var edited = box.Payload("edited", SmallPayload);
        PackageWriter.WriteManifest(edited);
        var package = box.PackageAsIs(edited, name: "edited.exe");
        var bytes = File.ReadAllBytes(package);
        bytes[^1] ^= 0x80;
        File.WriteAllBytes(package, bytes);
        result = box.Run(package, box.Switches());
        Assert.Equal(new SetupResult(ExitCode.DamagedPackage, "The installation package is damaged (payload.sha256)."), result);
        AssertNothingWritten(box);
    }

    [Fact]
    [Trait("Req", "INS-004")]
    public void TrailerMissingFails()
    {
        using var box = new SetupSandbox();
        var package = box.Package(box.Payload("good", SmallPayload));
        var bytes = File.ReadAllBytes(package);

        // no trailer at all (program + zip), a wrong magic, the program alone (an uninstall.exe), an empty file
        var cases = new Dictionary<string, byte[]>
        {
            ["no-trailer.exe"] = bytes[..^48],
            ["wrong-magic.exe"] = [.. bytes[..^48], .. "FXCSETUQ"u8, .. bytes[^40..]],
            ["program-only.exe"] = File.ReadAllBytes(box.FakeProgram()),
            ["empty.exe"] = [],
        };
        foreach (var (name, content) in cases)
        {
            var file = box.In(name);
            File.WriteAllBytes(file, content);
            var result = box.Run(file, box.Switches());
            Assert.Equal(new SetupResult(ExitCode.DamagedPackage, $"The installation package is damaged ({name})."), result);
            Assert.Equal(ExitCode.DamagedPackage, box.Run(file, ["/S", "/verify"]).Code);
            AssertNothingWritten(box);
        }

        // a zip offset that points past the zip
        var offset = bytes.AsSpan().ToArray();
        BinaryPrimitives.WriteInt64LittleEndian(offset.AsSpan(offset.Length - 40, 8), offset.Length);
        File.WriteAllBytes(box.In("bad-offset.exe"), offset);
        Assert.Equal(ExitCode.DamagedPackage, box.Run(box.In("bad-offset.exe"), box.Switches()).Code);
        AssertNothingWritten(box);
    }

    internal static void AssertNothingWritten(SetupSandbox box)
    {
        Assert.False(Directory.Exists(box.Home), "the target folder was created");
        Assert.Empty(Directory.EnumerateFileSystemEntries(box.StartMenu));
        Assert.False(box.RegistryRootExists(), "the test registry root was written");
    }
}
