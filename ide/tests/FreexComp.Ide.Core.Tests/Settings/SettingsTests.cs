using System.Text;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Settings;

[Trait("Phase", "1")]
public sealed class SettingsTests
{
    private const string UserDir = @"C:\u";
    private const string SettingsFile = @"C:\u\roaming\settings.json";

    private static readonly SettingKey<bool> Flag = SettingKey.Bool("t.flag", true);
    private static readonly SettingKey<int> Size = SettingKey.Int("t.size", 13, new SettingRange(6, 72));
    private static readonly SettingKey<string> Theme = SettingKey.Enum("t.theme", "system", "system", "light", "dark");
    private static readonly SettingKey<string> Color = SettingKey.Color("t.color", "#112233");
    private static readonly SettingKey<IReadOnlyList<string>> List = SettingKey.StringArray("t.list", "EAbort");
    private static readonly SettingKey<double> Ratio = SettingKey.Number("t.ratio", 0.45, new SettingRange(0.2, 0.8));
    private static readonly SettingKey<string> Name = SettingKey.String("t.name", "");
    private static readonly SettingKey<int> Interval =
        SettingKey.Int("t.interval", 30, new SettingRange(0, 0), new SettingRange(5, 600));

    private static readonly ISettingKey[] Keys = [Flag, Size, Theme, Color, List, Ratio, Name, Interval];

    private readonly InMemoryFileSystem _disk = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 13, 9, 15, 0, TimeSpan.Zero));
    private readonly RecordingLog _log = new();

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-020")]
    public void MissingFileYieldsDefaults()
    {
        var settings = Create();

        settings.Load();

        Assert.True(settings.Get(Flag));
        Assert.Equal(13, settings.Get(Size));
        Assert.Equal("system", settings.Get(Theme));
        Assert.Equal(["EAbort"], settings.Get(List));
        Assert.Equal(0.45, settings.Get(Ratio));
        Assert.True(settings.IsDefault(Size.Name));
        Assert.Null(settings.CorruptFileName);
        Assert.Empty(_log.Lines);
        Assert.False(_disk.DirectoryExists(@"C:\u\roaming")); // IDE-SET-001: nothing created at start-up
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-012")]
    public Task OnlyNonDefaultValuesAreWritten() => TestUiThread.RunAsync(() => OnlyNonDefaultValuesAreWrittenOnUiThread()); // I-209

    private async Task OnlyNonDefaultValuesAreWrittenOnUiThread()
    {
        var settings = Create();
        settings.Load();

        settings.Set(Size, 14);
        settings.Set(Flag, false);
        settings.Set(Flag, true); // back to default: no member
        await settings.FlushAsync();
        Assert.Equal("{\n  \"formatVersion\": 1,\n  \"t.size\": 14\n}\n", ReadFile());

        settings.Set(Size, 13);
        await settings.FlushAsync();
        Assert.Equal("{\n  \"formatVersion\": 1\n}\n", ReadFile()); // still written
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-010")]
    [Trait("Req", "IDE-SET-011")]
    [Trait("Req", "IDE-SET-014")]
    public Task KeysAreSortedAndFormatIsCanonical() => TestUiThread.RunAsync(() => KeysAreSortedAndFormatIsCanonicalOnUiThread()); // I-209

    private async Task KeysAreSortedAndFormatIsCanonicalOnUiThread()
    {
        var settings = Create();
        settings.Load();

        settings.Set(Theme, "dark");
        settings.Set(Color, "#a1b2c3");
        settings.Set(Ratio, 0.5);
        settings.Set(List, ["EAbort", "EFoo"]);
        settings.Set(Name, "Mono \"X\"");
        settings.Set(Flag, false);
        settings.Set(Size, 20);
        await settings.FlushAsync();

        Assert.Equal("#A1B2C3", settings.Get(Color));
        Assert.Equal(
            "{\n" +
            "  \"formatVersion\": 1,\n" +
            "  \"t.color\": \"#A1B2C3\",\n" +
            "  \"t.flag\": false,\n" +
            "  \"t.list\": [\"EAbort\", \"EFoo\"],\n" +
            "  \"t.name\": \"Mono \\\"X\\\"\",\n" +
            "  \"t.ratio\": 0.5,\n" +
            "  \"t.size\": 20,\n" +
            "  \"t.theme\": \"dark\"\n" +
            "}\n",
            ReadFile());
        Assert.NotEqual(0xEF, _disk.ReadAllBytes(SettingsFile)[0]); // no BOM
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-013")]
    public Task UnknownKeysArePreserved() => TestUiThread.RunAsync(() => UnknownKeysArePreservedOnUiThread()); // I-209

    private async Task UnknownKeysArePreservedOnUiThread()
    {
        WriteFile("{\r\n    \"zzz.future\": {\"a\": 1},\r\n    \"formatVersion\": 1,\r\n    \"aaa.old\": true,\r\n    \"t.size\": 20\r\n}");
        var settings = Create();
        settings.Load();

        settings.Set(Flag, false);
        await settings.FlushAsync();

        Assert.Equal(20, settings.Get(Size));
        Assert.Equal(
            "{\n" +
            "  \"formatVersion\": 1,\n" +
            "  \"aaa.old\": true,\n" +
            "  \"t.flag\": false,\n" +
            "  \"t.size\": 20,\n" +
            "  \"zzz.future\": {\n" +
            "    \"a\": 1\n" +
            "  }\n" +
            "}\n",
            ReadFile());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-022")]
    public Task InvalidValueFallsBackToDefault() => TestUiThread.RunAsync(() => InvalidValueFallsBackToDefaultOnUiThread()); // I-209

    private async Task InvalidValueFallsBackToDefaultOnUiThread()
    {
        WriteFile("""
            {
              "formatVersion": 1,
              "t.flag": "yes",
              "t.size": 100,
              "t.theme": "blue",
              "t.color": "#12345",
              "t.list": [1],
              "t.ratio": 0.1,
              "t.interval": 3,
              "t.name": "kept"
            }
            """);
        var settings = Create();
        settings.Load();

        Assert.True(settings.Get(Flag));
        Assert.Equal(13, settings.Get(Size)); // not clamped
        Assert.Equal("system", settings.Get(Theme));
        Assert.Equal("#112233", settings.Get(Color));
        Assert.Equal(["EAbort"], settings.Get(List));
        Assert.Equal(0.45, settings.Get(Ratio));
        Assert.Equal(30, settings.Get(Interval));
        Assert.Equal("kept", settings.Get(Name));
        Assert.Equal(
            new[] { "t.flag", "t.size", "t.theme", "t.color", "t.list", "t.ratio", "t.interval" }
                .Select(k => $"WARN settings: invalid value for '{k}', using default"),
            _log.Lines);

        settings.Set(Size, 12);
        await settings.FlushAsync();
        Assert.Equal("{\n  \"formatVersion\": 1,\n  \"t.name\": \"kept\",\n  \"t.size\": 12\n}\n", ReadFile());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-023")]
    public void KeysAreCaseSensitiveAndLastDuplicateWins()
    {
        WriteFile("{\"formatVersion\": 1, \"t.size\": 20, \"T.SIZE\": 30, \"t.size\": 21}");
        var settings = Create();

        settings.Load();

        Assert.Equal(21, settings.Get(Size));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-021")]
    public Task CorruptFileIsRenamedAndReported() => TestUiThread.RunAsync(() => CorruptFileIsRenamedAndReportedOnUiThread()); // I-209

    private async Task CorruptFileIsRenamedAndReportedOnUiThread()
    {
        WriteFile("{ \"t.size\": ");
        var settings = Create();

        settings.Load();

        Assert.Equal("settings.corrupt-20261113-091500.json", settings.CorruptFileName);
        Assert.False(_disk.FileExists(SettingsFile));
        Assert.Equal("{ \"t.size\": ", Encoding.UTF8.GetString(_disk.ReadAllBytes(@"C:\u\roaming\settings.corrupt-20261113-091500.json")));
        Assert.Equal(13, settings.Get(Size));

        settings.Set(Size, 14); // saving still works after a successful rename
        await settings.FlushAsync();
        Assert.Contains("\"t.size\": 14", ReadFile());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-021")]
    public Task CorruptFileThatCannotBeRenamedDisablesSaving() => TestUiThread.RunAsync(() => CorruptFileThatCannotBeRenamedDisablesSavingOnUiThread()); // I-209

    private async Task CorruptFileThatCannotBeRenamedDisablesSavingOnUiThread()
    {
        WriteFile("[1, 2]"); // root is not an object
        _disk.WriteAllBytes(@"C:\u\roaming\settings.corrupt-20261113-091500.json", [0]); // rename target taken
        var settings = Create();

        settings.Load();
        settings.Set(Size, 14);
        await settings.FlushAsync();

        Assert.Equal("settings.json", settings.CorruptFileName);
        Assert.Equal("[1, 2]", ReadFile());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-015")]
    public Task NewerFormatVersionIsNeverOverwritten() => TestUiThread.RunAsync(() => NewerFormatVersionIsNeverOverwrittenOnUiThread()); // I-209

    private async Task NewerFormatVersionIsNeverOverwrittenOnUiThread()
    {
        const string text = "{\"formatVersion\": 99, \"t.size\": 20, \"x.new\": 1}";
        WriteFile(text);
        var settings = Create();
        settings.Load();

        Assert.Equal(20, settings.Get(Size)); // known keys applied
        settings.Set(Size, 30);               // lasts for the session
        Assert.Equal(30, settings.Get(Size));
        await settings.FlushAsync();
        _clock.Advance(TimeSpan.FromSeconds(5));
        await settings.LastSaveTask;

        Assert.Equal(text, ReadFile());
        Assert.Contains("WARN settings: settings.json has newer formatVersion 99; changes will not be saved", _log.Lines);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-024")]
    [Trait("Req", "IDE-SET-025")]
    public void ChangeRaisesEventOnceAndAppliesLive()
    {
        var settings = Create();
        settings.Load();
        var events = new List<string>();
        settings.Changed += (_, e) => events.Add($"{e.Key}:{e.OldValue}->{e.NewValue}:{settings.Get(Size)}");
        settings.BatchCompleted += (_, _) => events.Add("batch");

        settings.Set(Size, 14);
        settings.Set(Size, 14); // equal: no event
        Assert.Equal(["t.size:13->14:14"], events);

        Assert.Throws<ArgumentException>(() => settings.Set(Size, 100));
        Assert.Throws<ArgumentException>(() => settings.Set(Theme, "blue"));
        Assert.Throws<ArgumentException>(() => settings.Set(Color, "red"));
        Assert.Equal(14, settings.Get(Size));

        events.Clear();
        using (settings.BeginBatch())
        {
            settings.Set(Size, 15);
            settings.Set(Size, 16);
            settings.Set(Theme, "dark");
            settings.Set(Theme, "system"); // net no change: no event
            Assert.Equal(16, settings.Get(Size)); // applied live inside the batch
            Assert.Empty(events);
        }

        Assert.Equal(["t.size:14->16:16", "batch"], events);

        events.Clear();
        settings.Set(Flag, false);
        settings.ResetAll("t.");
        Assert.Equal(["t.flag:False->True:13", "t.size:16->13:13", "batch"], events.Skip(1));
        Assert.True(settings.IsDefault(Size.Name));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-026")]
    [Trait("Req", "IDE-SET-027")]
    public Task SaveIsDebouncedAndAtomic() => TestUiThread.RunAsync(() => SaveIsDebouncedAndAtomicOnUiThread()); // I-209

    private async Task SaveIsDebouncedAndAtomicOnUiThread()
    {
        var spy = new RecordingFileSystem(_disk);
        var settings = Create(spy);
        settings.Load();

        settings.Set(Size, 14);
        _clock.Advance(TimeSpan.FromMilliseconds(400));
        settings.Set(Size, 15); // restarts the 500 ms window
        _clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.DoesNotContain(spy.Calls, c => c.StartsWith("WriteAllBytes", StringComparison.Ordinal));

        _clock.Advance(TimeSpan.FromMilliseconds(100));
        await settings.LastSaveTask;

        var writes = spy.Calls.Where(c => c.StartsWith("WriteAllBytes", StringComparison.Ordinal)
                                       || c.StartsWith("MoveFile", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [$"WriteAllBytes {SettingsFile}.tmp", $"MoveFile {SettingsFile}.tmp -> {SettingsFile}"],
            writes);
        Assert.False(_disk.FileExists(SettingsFile + ".tmp"));
        Assert.Contains("\"t.size\": 15", ReadFile());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-027")]
    public Task FailedSaveIsLoggedAndRetriedAtNextChange() => TestUiThread.RunAsync(() => FailedSaveIsLoggedAndRetriedAtNextChangeOnUiThread()); // I-209

    private async Task FailedSaveIsLoggedAndRetriedAtNextChangeOnUiThread()
    {
        var spy = new RecordingFileSystem(_disk) { FailWhen = c => c.StartsWith("MoveFile", StringComparison.Ordinal) };
        var settings = Create(spy);
        settings.Load();

        settings.Set(Size, 14);
        await settings.FlushAsync();
        Assert.Contains(_log.Lines, l => l.StartsWith("ERROR settings: ", StringComparison.Ordinal));
        Assert.False(_disk.FileExists(SettingsFile));

        spy.FailWhen = null;
        settings.Set(Flag, false);
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        await settings.LastSaveTask;
        Assert.Equal("{\n  \"formatVersion\": 1,\n  \"t.flag\": false,\n  \"t.size\": 14\n}\n", ReadFile());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-028")]
    public Task SecondInstanceKeepsKeysItDidNotChange() => TestUiThread.RunAsync(() => SecondInstanceKeepsKeysItDidNotChangeOnUiThread()); // I-209

    private async Task SecondInstanceKeepsKeysItDidNotChangeOnUiThread()
    {
        WriteFile("{\"formatVersion\": 1, \"t.name\": \"start\"}");
        var first = Create();
        var second = Create();
        first.Load();
        second.Load();

        first.Set(Size, 20);
        first.Set(Name, "first");
        await first.FlushAsync();
        second.Set(Flag, false);
        await second.FlushAsync();

        Assert.Equal(
            "{\n  \"formatVersion\": 1,\n  \"t.flag\": false,\n  \"t.name\": \"first\",\n  \"t.size\": 20\n}\n",
            ReadFile());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-002")]
    public Task UserDirOverrideIsHonoured() => TestUiThread.RunAsync(() => UserDirOverrideIsHonouredOnUiThread()); // I-209

    private async Task UserDirOverrideIsHonouredOnUiThread()
    {
        var spy = new RecordingFileSystem(_disk);
        var paths = UserPaths.Create(UserDir, _ => null, _ => throw new InvalidOperationException("real folder"));
        var settings = new SettingsService(paths, spy, _clock, _log, Keys);

        settings.Load();
        settings.Set(Size, 14);
        await settings.FlushAsync();

        Assert.True(_disk.FileExists(SettingsFile));
        Assert.All(spy.Calls, c => Assert.Contains(@"C:\u\roaming", c, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// IDE-TGT-001 (F01-010): <c>build.jobs</c> is an integer 0…256, default 0 (automatic); a stored value outside the range
    /// is replaced by the default when the settings are loaded; a set value is written and read back.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-TGT-001")]
    public Task BuildJobs_DefaultRangeAndRoundTrip() => TestUiThread.RunAsync(() => BuildJobsDefaultRangeAndRoundTripOnUiThread());

    private async Task BuildJobsDefaultRangeAndRoundTripOnUiThread()
    {
        var key = Assert.IsType<SettingKey<int>>(SettingsCatalog.All.Single(k => k.Name == "build.jobs"));
        Assert.Same(SettingsCatalog.BuildJobs, key);
        Assert.Equal((SettingType.Int, 0), (key.Type, key.Default));
        Assert.Equal([new SettingRange(0, 256)], key.Ranges);
        Assert.True(key.IsValid(0) && key.IsValid(256));
        Assert.False(key.IsValid(-1) || key.IsValid(257));

        SettingsService Load(string? file)
        {
            if (file is not null)
                WriteFile(file);
            var service = new SettingsService(UserPaths.Create(UserDir, _ => null, _ => throw new InvalidOperationException()), _disk, _clock, _log, [key]);
            service.Load();
            return service;
        }

        Assert.Equal(0, Load(null).Get(key)); // no file: automatic
        Assert.Equal(0, Load("{\"formatVersion\": 1, \"build.jobs\": 257}").Get(key));
        Assert.Equal(0, Load("{\"formatVersion\": 1, \"build.jobs\": -1}").Get(key));
        Assert.Equal(256, Load("{\"formatVersion\": 1, \"build.jobs\": 256}").Get(key));

        var settings = Load("{\"formatVersion\": 1}");
        Assert.Throws<ArgumentException>(() => settings.Set(key, 257));
        settings.Set(key, 4);
        await settings.FlushAsync();
        Assert.Equal("{\n  \"formatVersion\": 1,\n  \"build.jobs\": 4\n}\n", ReadFile());
        Assert.Equal(4, Load(null).Get(key)); // read back from the file just written

        settings.Set(key, 0);
        await settings.FlushAsync();
        Assert.Equal("{\n  \"formatVersion\": 1\n}\n", ReadFile()); // the default is not stored
    }

    private SettingsService Create(Core.Platform.IFileSystem? fs = null) =>
        new(UserPaths.Create(UserDir, _ => null, _ => throw new InvalidOperationException()), fs ?? _disk, _clock, _log, Keys);

    private void WriteFile(string text)
    {
        _disk.CreateDirectory(@"C:\u\roaming");
        _disk.WriteAllBytes(SettingsFile, Encoding.UTF8.GetBytes(text));
    }

    private string ReadFile() => Encoding.UTF8.GetString(_disk.ReadAllBytes(SettingsFile));
}
