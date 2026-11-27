using System.Text;
using FreexComp.Compiler.Toolchain;
using FreexComp.Tests.Shared;

namespace FreexComp.Build.Tests;

/// <summary>
/// resource-manager.md CMP-1429…1433 with the pinned tools: the <c>.pproj</c> keys <c>icon</c>, <c>versionInfo</c> and
/// <c>resources</c> end up in the image (card F11-091, acceptance 1–3).
/// </summary>
public sealed partial class PbuildCliTests
{
    private const string MyDataProgram = """
        program Res;

        uses
          Classes, Windows;

        var
          S: TResourceStream;
          B: Byte;
          I: Integer;
        begin
          S := TResourceStream.Create(HInstance, 'MYDATA', RT_RCDATA);
          try
            for I := 1 to S.Size do
            begin
              S.Read(B, 1);
              Write(Chr(B));
            end;
            Writeln;
          finally
            S.Free;
          end;
        end.

        """;

    private const string ResourceKeys = """
        "icon": "res/app.ico",
        "versionInfo": { "fileVersion": "1.2.3.4", "companyName": "Example \"Co\"", "fileDescription": "Resource sample",
                         "productName": "Res Product", "legalCopyright": "© 2026 Example", "comments": "made by a test" },
        "resources": [ { "path": "data/my data.txt", "type": "RCDATA", "name": "MyData" },
                       { "path": "res/other.ico", "type": "ICON", "name": "2" } ]
        """;

    /// <summary>Every line of the resource directory of an image as the object reader of the toolchain prints it, trimmed.</summary>
    private static async Task<string[]> ResourceDumpAsync(string image)
    {
        var reader = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin", "llvm-readobj.exe");
        var lines = new List<string>();
        void Line(string line)
        {
            lock (lines)
            {
                lines.Add(line.Trim());
            }
        }

        var exit = await ProcessRunner.Instance.RunAsync(reader, ["--coff-resources", image], Path.GetDirectoryName(image)!, Line, Line, TestContext.Current.CancellationToken);
        Assert.True(exit == 0, string.Join("\n", lines));
        return [.. lines];
    }

    /// <summary>The entry lines (<c>Name: MYDATA</c>, <c>Name: (ID 1)</c>) below the line of resource type <paramref name="type"/>.</summary>
    private static List<string> EntriesOf(string[] dump, string type)
    {
        var start = Array.FindIndex(dump, l => l.StartsWith("Type: " + type + " ", StringComparison.Ordinal));
        if (start < 0)
        {
            return [];
        }

        var entries = new List<string>();
        for (var i = start + 1; i < dump.Length && !dump[i].StartsWith("Type: ", StringComparison.Ordinal); i++)
        {
            if (dump[i].StartsWith("Name: ", StringComparison.Ordinal))
            {
                entries.Add(dump[i].TrimEnd(' ', '['));               // "Name: MYDATA [" -> "Name: MYDATA"
            }
        }

        return entries;
    }

    private static bool HasUtf16(byte[] image, string text) => image.AsSpan().IndexOf(Encoding.Unicode.GetBytes(text + "\0")) >= 0;

    private static string ResourceKeysProject(string dir, string keys)
    {
        File.WriteAllText(
            Path.Combine(dir, "Res.pproj"),
            $$"""{ "formatVersion": 1, "name": "Res", "mainSource": "Res.pas", {{keys}} }""".ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(dir, "Res.pas"), MyDataProgram.ReplaceLineEndings("\n"));
        Directory.CreateDirectory(Path.Combine(dir, "res"));
        Directory.CreateDirectory(Path.Combine(dir, "data"));
        File.WriteAllBytes(Path.Combine(dir, "res", "app.ico"), PrcCliTests.IconFile(1));
        File.WriteAllBytes(Path.Combine(dir, "res", "other.ico"), PrcCliTests.IconFile(1));
        File.WriteAllText(Path.Combine(dir, "data", "my data.txt"), "hello from MYDATA");
        return Path.Combine(dir, "bin", "Debug", "Res.exe");
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1429")]
    [Trait("Req", "CMP-1430")]
    public async Task ProjectResources_RcDataIconAndVersionInExe()
    {
        var dir = ResourceProjectDirectory();
        try
        {
            var exe = ResourceKeysProject(dir, ResourceKeys);

            var (code, lines) = PbuildReal(dir, "Res.pproj");

            Assert.True(code == 0, string.Join("\n", lines));
            Assert.DoesNotContain(lines, l => l.Contains("Res.project", StringComparison.Ordinal));      // not shown except in verbose output

            // the program reads MYDATA back from its own image
            var output = new List<string>();
            Assert.Equal(0, await NativeRun.RunAsync(exe, dir, output.Add, _ => { }));
            Assert.Equal(["hello from MYDATA"], output);

            // icon group 1 (the application icon) and 2, the named data entry, the version resource
            var dump = await ResourceDumpAsync(exe);
            var all = string.Join("\n", dump);
            Assert.True(EntriesOf(dump, "GROUP_ICON").SequenceEqual(["Name: (ID 1)", "Name: (ID 2)"]), all);
            Assert.True(EntriesOf(dump, "RCDATA").SequenceEqual(["Name: MYDATA"]), all);
            Assert.True(EntriesOf(dump, "VERSIONINFO").SequenceEqual(["Name: (ID 1)"]), all);
            var image = File.ReadAllBytes(exe);
            foreach (var (name, value) in new[]
                     {
                         ("CompanyName", "Example \"Co\""), ("FileDescription", "Resource sample"), ("FileVersion", "1.2.3.4"),
                         ("InternalName", "Res"), ("LegalCopyright", "© 2026 Example"), ("OriginalFilename", "Res.exe"),
                         ("ProductName", "Res Product"), ("ProductVersion", "1.2.3.4"), ("Comments", "made by a test"),
                     })
            {
                Assert.True(HasUtf16(image, name) && HasUtf16(image, value), $"{name} = {value}");
            }

            // VS_FIXEDFILEINFO: signature, structure version, file and product version 1.2.3.4, debug flag (Debug), Windows NT, application
            byte[] fixedInfo =
            [
                0xBD, 0x04, 0xEF, 0xFE, 0x00, 0x00, 0x01, 0x00, 2, 0, 1, 0, 4, 0, 3, 0, 2, 0, 1, 0, 4, 0, 3, 0,
                0x3F, 0, 0, 0, 1, 0, 0, 0, 0x04, 0, 0x04, 0, 1, 0, 0, 0,
            ];
            Assert.True(image.AsSpan().IndexOf(fixedInfo) >= 0, "VS_FIXEDFILEINFO not found");

            // second build: up to date, the generated script is not compiled again
            (code, lines) = PbuildReal(dir, "Res.pproj", "--verbose");
            Assert.True(code == 0, string.Join("\n", lines));
            Assert.Contains(lines, l => l.Contains("up to date", StringComparison.Ordinal));
            Assert.DoesNotContain("Compiling resources Res.project.rc", lines);
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1433")]
    public async Task RemovingAResourceRemovesItFromTheNextBuild()
    {
        var dir = ResourceProjectDirectory();
        try
        {
            var exe = ResourceKeysProject(dir, ResourceKeys);
            var (code, lines) = PbuildReal(dir, "Res.pproj");
            Assert.True(code == 0, string.Join("\n", lines));
            Assert.True(EntriesOf(await ResourceDumpAsync(exe), "RCDATA").SequenceEqual(["Name: MYDATA"]));

            // remove MYDATA → rebuild → no longer in the resource directory (the program now reports the missing resource)
            File.WriteAllText(
                Path.Combine(dir, "Res.pproj"),
                """{ "formatVersion": 1, "name": "Res", "mainSource": "Res.pas", "icon": "res/app.ico", "versionInfo": { "fileVersion": "2.0" } }""");
            (code, lines) = PbuildReal(dir, "Res.pproj");
            Assert.True(code == 0, string.Join("\n", lines));
            var dump = await ResourceDumpAsync(exe);
            Assert.True(EntriesOf(dump, "RCDATA").Count == 0, string.Join("\n", dump));
            Assert.True(EntriesOf(dump, "GROUP_ICON").SequenceEqual(["Name: (ID 1)"]), string.Join("\n", dump));
            Assert.True(HasUtf16(File.ReadAllBytes(exe), "2.0.0.0"));

            // remove all → the generated files are gone, the image has no icon and no version resource
            var obj = Path.Combine(dir, "obj", "Debug");
            Assert.NotEmpty(Directory.GetFiles(obj, "Res.project.*"));
            File.WriteAllText(Path.Combine(dir, "Res.pproj"), """{ "formatVersion": 1, "name": "Res", "mainSource": "Res.pas" }""");
            (code, lines) = PbuildReal(dir, "Res.pproj");
            Assert.True(code == 0, string.Join("\n", lines));
            Assert.Empty(Directory.GetFiles(obj, "Res.project.*"));
            dump = await ResourceDumpAsync(exe);
            Assert.DoesNotContain(dump, l => l.StartsWith("Type: GROUP_ICON", StringComparison.Ordinal) || l.StartsWith("Type: VERSIONINFO", StringComparison.Ordinal));
            Assert.Contains(dump, l => l.StartsWith("Type: MANIFEST", StringComparison.Ordinal));         // the generated manifest stays
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }
}
