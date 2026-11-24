using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using FreexComp.Build;
using FreexComp.Compiler;

namespace FreexComp.Policy.Tests;

/// <summary>
/// The fixture programs of the platform check suite (<c>tests/policy/Fixtures/platform/</c>), built with the in-process
/// build engine and the pinned toolchain in a temporary directory: once per test run, and only when a test asks for them.
/// </summary>
public sealed class PlatformPrograms : IDisposable
{
    public const string Console = "PlatConsole";
    public const string Gui = "PlatGui";
    public const string Admin = "PlatAdmin";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "freexcomp-platform-" + Guid.NewGuid().ToString("N"));
    private readonly Lazy<Task<IReadOnlyDictionary<string, string>>> _built;

    public PlatformPrograms() => _built = new(BuildAllAsync);

    /// <summary>The path of the executable file of one of the three fixtures as the repository holds it.</summary>
    public async Task<string> ExeAsync(string name) => (await _built.Value)[name];

    /// <summary>The bytes of that executable file.</summary>
    public async Task<byte[]> ImageAsync(string name) => await File.ReadAllBytesAsync(await ExeAsync(name));

    /// <summary>Builds a copy of the fixture <paramref name="name"/> whose project has the given <c>manifest</c> object.</summary>
    public Task<string> BuildVariantAsync(string name, string variant, JsonObject manifest) => BuildAsync(name, variant, manifest);

    private async Task<IReadOnlyDictionary<string, string>> BuildAllAsync()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in (string[])[Console, Gui, Admin])
        {
            result[name] = await BuildAsync(name, "default", manifest: null);
        }

        return result;
    }

    private async Task<string> BuildAsync(string name, string variant, JsonObject? manifest)
    {
        var toolchain = RepoPaths.Combine("third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(toolchain, "clang.exe")), $"Pinned toolchain missing in {toolchain} (run scripts/bootstrap.ps1).");

        var directory = Path.Combine(_directory, variant, name);
        Directory.CreateDirectory(directory);
        var source = RepoPaths.Combine("tests", "policy", "Fixtures", "platform");
        File.Copy(Path.Combine(source, name + ".pas"), Path.Combine(directory, name + ".pas"));
        var project = File.ReadAllText(Path.Combine(source, name + ".pproj"));
        if (manifest is not null)
        {
            var json = JsonNode.Parse(project)!.AsObject();
            json["manifest"] = manifest;
            project = json.ToJsonString();
        }

        File.WriteAllText(Path.Combine(directory, name + ".pproj"), project, new UTF8Encoding(false));

        var log = new EventLog();
        var result = await new BuildEngine(PhysicalFileSystem.Instance).RunAsync(
            new BuildRequest
            {
                ProjectPath = Path.Combine(directory, name + ".pproj"),
                ToolchainDirectory = toolchain,
                RtlDirectory = RepoPaths.Combine("rtl", "src"),
            },
            log,
            CancellationToken.None);
        Assert.True(result.Success, name + ": " + string.Join("\n", log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToString())));
        var exe = Assert.IsType<string>(result.OutputPath);
        Assert.True(File.Exists(exe), exe + " was not written");
        return exe;
    }

    /// <summary>
    /// Starts <paramref name="exe"/> and waits for its end; a program still running at the <paramref name="deadline"/> is
    /// killed and the test fails, so no process is left behind.
    /// </summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(string exe, IEnumerable<string> arguments, TimeSpan deadline, CancellationToken ct)
    {
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var stdout = FreexComp.Tests.Shared.ChildPipe.ReadToEndOnThread(process.StandardOutput, "stdout");
        var stderr = FreexComp.Tests.Shared.ChildPipe.ReadToEndOnThread(process.StandardError, "stderr");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(deadline);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            Assert.Fail($"{Path.GetFileName(exe)} did not end within {deadline.TotalSeconds:0} s and was stopped");
        }

        return (process.ExitCode, await stdout + await stderr);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class EventLog : IProgress<BuildEvent>
    {
        private readonly ConcurrentQueue<BuildEvent> _events = new();

        public IReadOnlyList<BuildEvent> Events => [.. _events];

        public void Report(BuildEvent value) => _events.Enqueue(value);
    }
}

/// <summary>The fields of a PE image that the platform check suite reads: file and optional header, import directory.</summary>
internal sealed class PeImage
{
    private readonly byte[] _pe;
    private readonly int _header;
    private readonly int _optional;

    public PeImage(byte[] pe)
    {
        _pe = pe;
        _header = BitConverter.ToInt32(pe, 0x3C);
        Assert.True(BitConverter.ToUInt32(pe, _header) == 0x4550, "no PE signature");
        _optional = _header + 24;
    }

    public ushort Machine => U16(_header + 4);

    public ushort Characteristics => U16(_header + 22);

    /// <summary>0x20B = PE32+.</summary>
    public ushort Magic => U16(_optional);

    public (ushort Major, ushort Minor) OperatingSystemVersion => (U16(_optional + 40), U16(_optional + 42));

    public (ushort Major, ushort Minor) SubsystemVersion => (U16(_optional + 48), U16(_optional + 50));

    public ushort Subsystem => U16(_optional + 68);

    public ushort DllCharacteristics => U16(_optional + 70);

    /// <summary>The lower-cased library names of the import directory (data directory 1), in file order.</summary>
    public IReadOnlyList<string> ImportedLibraries()
    {
        Assert.Equal(0x20B, Magic);
        var names = new List<string>();
        var importRva = I32(_optional + 112 + 8);
        if (importRva == 0)
        {
            return names;
        }

        for (var entry = Offset(importRva); I32(entry + 12) != 0; entry += 20)
        {
            var name = Offset(I32(entry + 12));
            var end = Array.IndexOf(_pe, (byte)0, name);
            names.Add(Encoding.ASCII.GetString(_pe, name, end - name).ToLowerInvariant());
        }

        return names;
    }

    private ushort U16(int at) => BitConverter.ToUInt16(_pe, at);

    private int I32(int at) => BitConverter.ToInt32(_pe, at);

    private int Offset(int rva)
    {
        var sections = U16(_header + 6);
        var table = _optional + U16(_header + 20);
        for (var i = 0; i < sections; i++)
        {
            var s = table + (i * 40);
            var address = I32(s + 12);
            if (rva >= address && rva < address + Math.Max(I32(s + 8), I32(s + 16)))
            {
                return rva - address + I32(s + 20);
            }
        }

        throw new InvalidOperationException($"RVA 0x{rva:X} is in no section");
    }
}
