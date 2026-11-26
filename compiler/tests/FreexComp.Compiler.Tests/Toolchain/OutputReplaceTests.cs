using System.Text;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>
/// C-362 (CMP-806): an output file that another process holds for a moment — a program that has just exited, a scanner — is
/// replaced once it is released; one that stays held is <c>E4008</c> after the bounded wait. The waits run on a simulated clock:
/// the holder lets go when the file system has waited 200 ms.
/// </summary>
[Trait("Phase", "3")]
[Trait("Req", "CMP-806")]
public sealed class OutputReplaceTests : IDisposable
{
    private static readonly TimeSpan Release = TimeSpan.FromMilliseconds(200);
    private readonly string _dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));

    public OutputReplaceTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "obj"));
        Directory.CreateDirectory(Path.Combine(_dir, "bin"));
        File.WriteAllText(Exe, "old");
    }

    private string Exe => Path.Combine(_dir, "bin", "P.exe");

    [Fact]
    public async Task LockedOutput_IsReplacedAfterRelease()
    {
        using var holder = Hold(Exe);
        var waited = TimeSpan.Zero;
        var fs = new PhysicalFileSystem(wait: d =>
        {
            waited += d;
            if (waited >= Release)
            {
                holder.Dispose();
            }
        });
        var linker = LinkerWritingTheExe();

        var result = await Link(fs, linker);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal("new", File.ReadAllText(Exe));
        Assert.Single(linker.Calls);
        Assert.InRange(waited, Release, FileRetry.Delays.Aggregate(TimeSpan.Zero, (a, b) => a + b) - TimeSpan.FromTicks(1));
    }

    [Fact]
    public async Task LockedOutput_StillLocked_ReportsError()
    {
        using var holder = Hold(Exe);
        var waited = TimeSpan.Zero;
        var fs = new PhysicalFileSystem(wait: d => waited += d);
        var linker = LinkerWritingTheExe();

        var result = await Link(fs, linker);

        Assert.False(result.Success);
        Assert.Equal(
            [$"{Main}(0,0): error E4008: Cannot write output file '{Exe}': the file is in use by another process"],
            result.Diagnostics.Select(d => d.ToText()));
        Assert.Empty(linker.Calls);
        Assert.Equal(TimeSpan.FromSeconds(2), waited);                          // the bounded wait, then the existing error
        holder.Dispose();
        Assert.Equal("old", File.ReadAllText(Exe));
    }

    [Fact]
    public void LockedFile_WriteAllBytes_IsReplacedAfterRelease()
    {
        var file = Path.Combine(_dir, "obj", "P.pcu");
        File.WriteAllText(file, "old");
        using var holder = Hold(file);
        var waited = TimeSpan.Zero;
        var fs = new PhysicalFileSystem(wait: d =>
        {
            waited += d;
            if (waited >= Release)
            {
                holder.Dispose();
            }
        });

        fs.WriteAllBytes(file, Encoding.UTF8.GetBytes("new"));

        Assert.Equal("new", File.ReadAllText(file));
        Assert.False(File.Exists(file + ".tmp"));
    }

    public void Dispose() => TestDirectories.Delete(_dir);

    private static readonly string Main = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "P.pas"));

    private static FileStream Hold(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.None);

    private FakeProcessRunner LinkerWritingTheExe() => new((_, _) =>
    {
        File.WriteAllText(Exe, "new");
        return Task.FromResult((0, Array.Empty<string>(), Array.Empty<string>()));
    });

    private Task<LinkResult> Link(IFileSystem fs, FakeProcessRunner linker) =>
        new LinkInvoker(new ToolRunner(linker, new ToolRunOptions(Location.ForFile(Main))), @"C:\llvm\bin\lld-link.exe", fs)
            .LinkAsync(new LinkRequest(Exe, Path.Combine(_dir, "obj"), [Path.Combine(_dir, "obj", "P.obj")]), TestContext.Current.CancellationToken);
}
