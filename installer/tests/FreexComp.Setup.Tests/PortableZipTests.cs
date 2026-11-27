using System.IO.Compression;
using System.Text;

namespace FreexComp.Setup.Tests;

/// <summary>
/// INS-020: the portable zip is the staging folder plus the marker <c>portable.json</c> at its root, without the files
/// only a setup run writes (<c>install.json</c>, <c>uninstall.exe</c>). Checked on a small payload; the real zip is
/// unpacked and used by <see cref="EndToEndTests"/>.
/// </summary>
[Trait("Phase", "8")]
public sealed class PortableZipTests
{
    [Fact]
    [Trait("Req", "INS-001")]
    [Trait("Req", "INS-020")]
    public void HoldsThePayloadAndTheMarker()
    {
        using var box = new SetupSandbox();
        var payload = box.Payload("portable", ManifestTests.SmallPayload);
        PackageWriter.WriteManifest(payload);
        var manifest = File.ReadAllBytes(Path.Combine(payload, PackageFormat.ManifestName));
        var zipFile = box.In("portable.zip");
        PackageWriter.WritePortableZip(payload, zipFile);

        // the staging folder itself got no marker: the setup package built from it must not carry one
        Assert.False(File.Exists(Path.Combine(payload, PackageWriter.PortableMarkerName)));

        using (var zip = ZipFile.OpenRead(zipFile))
        {
            // every payload file and the manifest at the root of the zip, plus the marker; sorted, "/" separated
            var expected = ManifestTests.SmallPayload.Keys.Append(PackageFormat.ManifestName).Append("portable.json")
                .Order(StringComparer.OrdinalIgnoreCase).Select(p => p.Replace('\\', '/')).ToList();
            Assert.Equal(expected, zip.Entries.Select(e => e.FullName));
            Assert.DoesNotContain(zip.Entries, e => e.Name.Equals("install.json", StringComparison.OrdinalIgnoreCase)
                                                   || e.Name.Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase));
            Assert.All(zip.Entries, e => Assert.Equal(PackageWriter.EntryTime.DateTime, e.LastWriteTime.DateTime));   // the stored clock time
            Assert.Equal("{ \"formatVersion\": 1 }\n", Read(zip, "portable.json"));
            foreach (var (path, content) in ManifestTests.SmallPayload)
                Assert.Equal(content, Read(zip, path.Replace('\\', '/')));
        }

        // unzipped, the folder is the staging folder plus the marker, and its manifest still describes the payload
        var home = box.In(SetupSandbox.SpacesAndNonAscii);
        ZipFile.ExtractToDirectory(zipFile, home);
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(home, PackageFormat.ManifestName)));
        var entries = PackageFormat.ParseManifest(manifest);
        Assert.NotNull(entries);
        Assert.Equal(ManifestTests.SmallPayload.Count, entries.Count);
        foreach (var entry in entries)
            Assert.Equal(entry.Hash, PackageFormat.HashFile(Path.Combine(home, entry.Path)));

        // same inputs, byte-identical zip (INS-001)
        File.SetLastWriteTimeUtc(Path.Combine(payload, "opc.exe"), DateTime.UtcNow.AddDays(-3));
        var again = box.In("again.zip");
        PackageWriter.WritePortableZip(payload, again);
        Assert.Equal(File.ReadAllBytes(zipFile), File.ReadAllBytes(again));
    }

    [Theory]
    [Trait("Req", "INS-020")]
    [InlineData("portable.json")]
    [InlineData("install.json")]
    [InlineData("uninstall.exe")]
    [InlineData("Portable.JSON")]
    public void StagingFolderWithASetupOrMarkerFileIsRefused(string name)
    {
        using var box = new SetupSandbox();
        var files = new Dictionary<string, string>(ManifestTests.SmallPayload) { [name] = "left over" };
        var payload = box.Payload("dirty", files);
        PackageWriter.WriteManifest(payload);
        var zipFile = box.In("portable.zip");
        var error = Assert.Throws<InvalidOperationException>(() => PackageWriter.WritePortableZip(payload, zipFile));
        Assert.Contains(name, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(zipFile));
    }

    [Fact]
    [Trait("Req", "INS-020")]
    public void StagingFolderWithoutManifestIsRefused()
    {
        using var box = new SetupSandbox();
        var payload = box.Payload("no-manifest", ManifestTests.SmallPayload);
        var zipFile = box.In("portable.zip");
        var error = Assert.Throws<InvalidOperationException>(() => PackageWriter.WritePortableZip(payload, zipFile));
        Assert.Contains(PackageFormat.ManifestName, error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(zipFile));
    }

    private static string Read(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open(), new UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}
