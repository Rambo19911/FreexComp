using System.Text;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>resource-compiler.md §7: the reader of compiled resource files.</summary>
[Trait("Phase", "8")]
[Trait("Req", "CMP-1321")]
public sealed class ResourceFileTests
{
    private static readonly byte[] Empty =
        [0, 0, 0, 0, 0x20, 0, 0, 0, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>One entry as the format describes it: sizes, type, name, padding, the 16 fixed bytes, data, padding.</summary>
    internal static byte[] Entry(object type, object name, ushort language, byte[] data)
    {
        var header = new List<byte>();
        foreach (var id in new[] { type, name })
        {
            if (id is int number)
            {
                header.AddRange([0xFF, 0xFF, (byte)number, (byte)(number >> 8)]);
            }
            else
            {
                header.AddRange(Encoding.Unicode.GetBytes((string)id + "\0"));
            }
        }

        while ((header.Count + 8) % 4 != 0)
        {
            header.Add(0);
        }

        header.AddRange([0, 0, 0, 0, 0x30, 0, (byte)language, (byte)(language >> 8), 0, 0, 0, 0, 0, 0, 0, 0]);
        var entry = new List<byte>();
        entry.AddRange(BitConverter.GetBytes(data.Length));
        entry.AddRange(BitConverter.GetBytes(header.Count + 8));
        entry.AddRange(header);
        entry.AddRange(data);
        while (entry.Count % 4 != 0)
        {
            entry.Add(0);
        }

        return [.. entry];
    }

    [Fact]
    public void Read_TypesNamesLanguages()
    {
        byte[] file =
        [
            .. Empty,
            .. Entry(10, 1, 0x0409, [1, 2, 3, 4, 5]),
            .. Entry("MYTYPE", "LOGO", 0x0415, [9, 9]),
            .. Entry(24, "ODD", 0, []),
        ];

        var entries = ResourceFile.Read(file);

        Assert.Equal(
            [
                new ResourceEntry(10, 1, 0x0409, 5),
                new ResourceEntry("MYTYPE", "LOGO", 0x0415, 2),
                new ResourceEntry(24, "ODD", 0, 0),
            ],
            entries);
        Assert.Equal(["10 1", "\"MYTYPE\" \"LOGO\"", "24 \"ODD\""], entries.Select(e => $"{e.Type} {e.Name}"));
        Assert.Empty(ResourceFile.Read(Empty));
    }

    public static TheoryData<string, byte[]> InvalidFiles => new()
    {
        { "shorter than a header", Empty[..20] },
        { "header size smaller than the fixed part", [.. Empty, 0, 0, 0, 0, 0x10, 0, 0, 0, 0xFF, 0xFF, 1, 0, 0xFF, 0xFF, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] },
        { "entry reaching past the end", [.. Empty, .. Entry(10, 1, 0, new byte[64])[..40]] },
        { "first entry not the empty entry", Entry(10, 1, 0, [1, 2, 3, 4]) },
    };

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public void Invalid_IsRejected(string what, byte[] file)
    {
        Assert.True(what.Length > 0);
        Assert.Throws<InvalidDataException>(() => ResourceFile.Read(file));
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    public async Task ReadsWhatTheRealToolWrites()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var script = Path.Combine(dir, "src", "real.rc");
            PhysicalFileSystem.Instance.WriteAllBytes(script, Encoding.UTF8.GetBytes("""
                #include <windows.h>
                7 RCDATA BEGIN "abc", 1, 2L END
                LOGO MYTYPE BEGIN 1 END
                LANGUAGE 21, 1
                3 300 BEGIN "xy" END
                4 RT_RCDATA BEGIN 1 END

                """));
            var request = new ResourceCompileRequest(script, Path.Combine(dir, "obj", "real.res"), Path.Combine(dir, "obj"));
            var compiler = new ResourceCompiler(
                new ToolRunner(ProcessRunner.Instance, new ToolRunOptions(Location.ForFile(script))),
                new ToolchainPaths(Path.GetDirectoryName(Clang.Path)!),
                PhysicalFileSystem.Instance);

            var result = await compiler.CompileAsync(request, TestContext.Current.CancellationToken);

            Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
            Assert.Equal(
                [
                    new ResourceEntry(10, 7, 0x0409, 9),                    // "abc" + a 16-bit and a 32-bit number
                    new ResourceEntry("MYTYPE", "LOGO", 0x0409, 2),
                    new ResourceEntry(300, 3, 0x0415, 2),
                    new ResourceEntry(10, 4, 0x0415, 2),
                ],
                ResourceFile.Read(File.ReadAllBytes(request.OutputPath)));
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }
}
