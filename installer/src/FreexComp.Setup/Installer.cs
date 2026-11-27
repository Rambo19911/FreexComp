using System.Security;
using Microsoft.Win32;

namespace FreexComp.Setup;

/// <summary>Progress of a run: the step shown to the user and the fraction (0…1) of the whole run.</summary>
public sealed record InstallProgress(string Step, double Fraction);

/// <summary>Install (INS-007…010) and uninstall (INS-011) of a FreexComp home folder H.</summary>
public sealed class Installer(SetupEnvironment environment)
{
    public const string ProductName = "FreexComp";
    public const string Publisher = "FreexComp project";
    public const string IdeProgram = "FreexComp.Ide.exe";
    public const string ShortcutName = "FreexComp.lnk";
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FreexComp";

    /// <summary>INS-008: extension, ProgId, type name, whether the extension's default value is set (only when it has none).</summary>
    public static readonly IReadOnlyList<(string Extension, string ProgId, string TypeName, bool SetDefault)> Associations =
    [
        (".pproj", "FreexComp.Project", "FreexComp project", true),
        (".pgroup", "FreexComp.ProjectGroup", "FreexComp project group", true),
        (".pas", "FreexComp.Source", "FreexComp source file", false),
        (".form", "FreexComp.Form", "FreexComp form", false),
    ];

    public string DefaultTarget(bool allUsers) => allUsers
        ? Path.Combine(environment.Folder("ProgramFiles"), ProductName)
        : Path.Combine(environment.Folder("LOCALAPPDATA"), "Programs", ProductName);

    public string StartMenuDirectory(SetupOptions options) => options.StartMenuDirectory
        ?? Path.Combine(environment.Folder(options.AllUsers ? "ProgramData" : "APPDATA"), "Microsoft", "Windows", "Start Menu", "Programs");

    public static RegistryRoot RootOf(SetupOptions options) => options.RegistryRoot is { } test
        ? new RegistryRoot(Registry.CurrentUser, test)
        : new RegistryRoot(options.AllUsers ? Registry.LocalMachine : Registry.CurrentUser, "");

    /// <summary>
    /// INS-007: verifies the whole package first (nothing is written before), then replaces an install already in H (a
    /// non-empty H without <c>install.json</c> is refused, exit 4), extracts, writes <c>uninstall.exe</c>, the shortcut, the
    /// registry and <c>install.json</c>. A failure or cancellation removes what this run wrote.
    /// </summary>
    public SetupResult Install(SetupPackage package, SetupOptions options, IProgress<InstallProgress>? progress = null, CancellationToken cancel = default)
    {
        var home = options.TargetDirectory ?? DefaultTarget(options.AllUsers);
        progress?.Report(new InstallProgress("Checking the installation package…", 0));
        package.Verify(new Progress(progress, "Checking the installation package…", 0, 0.3), cancel);

        if (File.Exists(home))
            throw new SetupException(ExitCode.IoError, $"Cannot install into {home}: it is a file.");
        var existed = Directory.Exists(home);
        InstallRecord? old = null;
        if (existed && Directory.EnumerateFileSystemEntries(home).Any())
        {
            old = InstallRecord.Read(home)
                  ?? throw new SetupException(ExitCode.TargetNotEmpty, $"The folder {home} is not empty and holds no FreexComp installation.");
        }

        cancel.ThrowIfCancellationRequested();
        if (old is not null)
        {
            progress?.Report(new InstallProgress("Removing the installed version…", 0.3));
            Remove(old, home, removeHome: false);
        }

        var root = RootOf(options);
        var record = new InstallRecord { Version = environment.Version, Scope = options.AllUsers ? "machine" : "user", RegistryRoot = root.Name };
        try
        {
            Directory.CreateDirectory(home);
            package.Extract(home, record.Files.Add, new Progress(progress, "Copying files…", 0.3, 0.65), cancel);
            cancel.ThrowIfCancellationRequested();

            progress?.Report(new InstallProgress("Registering…", 0.95));
            var uninstaller = Path.Combine(home, InstallRecord.UninstallProgram);
            record.Files.Add(InstallRecord.UninstallProgram);
            package.CopyProgramTo(uninstaller);
            var ide = Path.Combine(home, IdeProgram);
            if (!options.NoShortcuts)
            {
                var folder = StartMenuDirectory(options);
                record.Shortcut = Path.Combine(folder, ShortcutName);
                Directory.CreateDirectory(folder);
                Shortcut.Create(record.Shortcut, ide, ProductName);
            }

            if (!options.NoAssociations)
                WriteAssociations(root, ide, record);
            var size = (package.PayloadBytes + new FileInfo(uninstaller).Length + 1023) / 1024;
            WriteUninstallEntry(root, home, ide, uninstaller, size, record);
            File.WriteAllText(Path.Combine(home, InstallRecord.FileName), record.ToJson());
            if (!options.NoAssociations && options.RegistryRoot is null && environment.NotifyShell)
                Shell.AssociationsChanged();
        }
        catch (Exception e)
        {
            try
            {
                Remove(record, home, removeHome: !existed);
            }
            catch (SetupException)
            {
                // the first failure is the one reported
            }

            throw e switch
            {
                SetupException or OperationCanceledException => e,
                IOException or UnauthorizedAccessException or SecurityException => new SetupException(ExitCode.IoError, e.Message, e),
                _ => e,
            };
        }

        progress?.Report(new InstallProgress("Done.", 1));
        return SetupResult.Ok($"FreexComp {environment.Version} was installed in {home}.");
    }

    /// <summary>
    /// INS-011: removes exactly what <paramref name="record"/> lists — files, folders of H they leave empty, H itself when
    /// empty (<paramref name="removeHome"/>), the shortcut, the registry values (a default only while it still equals ours)
    /// and keys, and the uninstall entry last. A file that cannot be deleted stops the removal (exit 6, <c>install.json</c>
    /// stays, so a later run continues).
    /// </summary>
    public static void Remove(InstallRecord record, string home, bool removeHome)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in record.Files.Append(InstallRecord.FileName))
        {
            if (!PackageFormat.IsSafeRelativePath(file))
                continue;   // never outside H, whatever the record says
            var path = Path.Combine(home, file);
            DeleteFile(path, file.Equals(InstallRecord.UninstallProgram, StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
            for (var folder = Path.GetDirectoryName(path); folder is not null && folder.Length > home.Length; folder = Path.GetDirectoryName(folder))
                folders.Add(folder);
        }

        foreach (var folder in folders.OrderByDescending(f => f.Length))
            DeleteEmptyFolder(folder);
        if (removeHome)
            DeleteEmptyFolder(home);
        if (record.Shortcut is { } shortcut && Path.GetFileName(shortcut).Equals(ShortcutName, StringComparison.OrdinalIgnoreCase))
            DeleteFile(shortcut, TimeSpan.Zero);

        var root = RegistryRoot.Parse(record.RegistryRoot);
        try
        {
            foreach (var value in Enumerable.Reverse(record.Values))
                root.Restore(value);
            foreach (var key in Enumerable.Reverse(record.Keys).Where(k => !IsUninstallKey(k)))
                root.Delete(key);
            foreach (var key in record.Keys.Where(IsUninstallKey))
                root.Delete(key);
            // the keys above the uninstall entry this install created are empty only now
            foreach (var key in Enumerable.Reverse(record.Keys).Where(k => !k.Tree))
                root.Delete(key);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new SetupException(ExitCode.IoError, $"Cannot change the registry under {root.Name}: {e.Message}", e);
        }
    }

    private static bool IsUninstallKey(RegistryKeyRecord key) => key.Key.Equals(UninstallKey, StringComparison.OrdinalIgnoreCase);

    private static void WriteAssociations(RegistryRoot root, string ide, InstallRecord record)
    {
        foreach (var (extension, progId, typeName, setDefault) in Associations)
        {
            var progKey = $@"Software\Classes\{progId}";
            root.CreateKey(progKey, tree: true, record);
            root.SetValue(progKey, "", typeName);
            root.SetValue(progKey + @"\DefaultIcon", "", $"{ide},0");
            root.SetValue(progKey + @"\shell\open\command", "", $"\"{ide}\" \"%1\"");

            var extensionKey = $@"Software\Classes\{extension}";
            root.CreateKey(extensionKey, tree: false, record);
            if (setDefault && root.Text(extensionKey, "") is var current && string.IsNullOrEmpty(current))
            {
                record.Values.Add(new RegistryValueRecord(extensionKey, "", progId, current));
                root.SetValue(extensionKey, "", progId);
            }

            var openWith = extensionKey + @"\OpenWithProgids";
            root.CreateKey(openWith, tree: false, record);
            if (root.Text(openWith, progId) is null)
            {
                record.Values.Add(new RegistryValueRecord(openWith, progId, "", null));
                root.SetEmptyValue(openWith, progId);
            }
        }
    }

    private static void WriteUninstallEntry(RegistryRoot root, string home, string ide, string uninstaller, long sizeKb, InstallRecord record)
    {
        root.CreateKey(UninstallKey, tree: true, record);
        root.SetValue(UninstallKey, "DisplayName", ProductName);
        root.SetValue(UninstallKey, "DisplayVersion", record.Version);
        root.SetValue(UninstallKey, "Publisher", Publisher);
        root.SetValue(UninstallKey, "InstallLocation", home);
        root.SetValue(UninstallKey, "UninstallString", $"\"{uninstaller}\" /uninstall");
        root.SetValue(UninstallKey, "QuietUninstallString", $"\"{uninstaller}\" /uninstall /S");
        root.SetValue(UninstallKey, "DisplayIcon", $"{ide},0");
        root.SetDword(UninstallKey, "EstimatedSize", (int)Math.Min(sizeKb, int.MaxValue));
        root.SetDword(UninstallKey, "NoModify", 1);
        root.SetDword(UninstallKey, "NoRepair", 1);
    }

    private static void DeleteFile(string path, TimeSpan patience)
    {
        var until = DateTime.UtcNow + patience;
        while (true)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= until)
                    throw new SetupException(ExitCode.IoError, $"Cannot delete {path}: {e.Message}", e);
                Thread.Sleep(100);
            }
        }
    }

    private static void DeleteEmptyFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a folder in use or filled meanwhile stays
        }
    }

    /// <summary>Maps the progress of one step onto its share of the whole run.</summary>
    private sealed class Progress(IProgress<InstallProgress>? target, string step, double start, double share) : IProgress<double>
    {
        public void Report(double value) => target?.Report(new InstallProgress(step, start + share * value));
    }
}

/// <summary>The registry root of an install: a hive (HKCU / HKLM) or, in tests, a key below HKCU (INS-005 <c>/regroot=</c>).</summary>
public sealed class RegistryRoot(RegistryKey hive, string prefix)
{
    public string Name => prefix.Length == 0 ? hive.Name : hive.Name + @"\" + prefix;

    public static RegistryRoot Parse(string name)
    {
        var parts = name.Split('\\', 2);
        var hive = parts[0] switch
        {
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            _ => throw new SetupException(ExitCode.IoError, $"Unknown registry root {name}."),
        };
        return new RegistryRoot(hive, parts.Length > 1 ? parts[1] : "");
    }

    private string Full(string key) => prefix.Length == 0 ? key : prefix + @"\" + key;

    public bool Exists(string key)
    {
        using var opened = hive.OpenSubKey(Full(key));
        return opened is not null;
    }

    /// <summary>
    /// Creates <paramref name="key"/>, recording every key of its path that did not exist (outer first), and the key
    /// itself always when it belongs to the product (<paramref name="tree"/>).
    /// </summary>
    public void CreateKey(string key, bool tree, InstallRecord record)
    {
        var parts = key.Split('\\');
        for (var i = 1; i <= parts.Length; i++)
        {
            var path = string.Join('\\', parts[..i]);
            var own = tree && i == parts.Length;
            if ((own || !Exists(path)) && !record.Keys.Contains(new RegistryKeyRecord(path, own)))
                record.Keys.Add(new RegistryKeyRecord(path, own));
        }

        using var _ = hive.CreateSubKey(Full(key), writable: true);
    }

    public void SetValue(string key, string name, string data)
    {
        using var opened = hive.CreateSubKey(Full(key), writable: true);
        opened.SetValue(name, data, RegistryValueKind.String);
    }

    public void SetDword(string key, string name, int data)
    {
        using var opened = hive.CreateSubKey(Full(key), writable: true);
        opened.SetValue(name, data, RegistryValueKind.DWord);
    }

    /// <summary>An empty REG_NONE value (the customary form of <c>OpenWithProgids</c> entries).</summary>
    public void SetEmptyValue(string key, string name)
    {
        using var opened = hive.CreateSubKey(Full(key), writable: true);
        opened.SetValue(name, Array.Empty<byte>(), RegistryValueKind.None);
    }

    /// <summary>A value as text ("" for an empty binary value), or null when the key or value does not exist.</summary>
    public string? Text(string key, string name)
    {
        using var opened = hive.OpenSubKey(Full(key));
        return opened?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) switch
        {
            null => null,
            byte[] { Length: 0 } => "",
            var value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Undoes a written value while it still holds our data: the previous value comes back, or the value goes.</summary>
    public void Restore(RegistryValueRecord value)
    {
        if (Text(value.Key, value.Name) != value.Data)
            return;
        using var opened = hive.OpenSubKey(Full(value.Key), writable: true);
        if (opened is null)
            return;
        if (value.Previous is null)
            opened.DeleteValue(value.Name, throwOnMissingValue: false);
        else
            opened.SetValue(value.Name, value.Previous, RegistryValueKind.String);
    }

    public void Delete(RegistryKeyRecord key)
    {
        if (key.Tree)
        {
            hive.DeleteSubKeyTree(Full(key.Key), throwOnMissingSubKey: false);
            return;
        }

        using (var opened = hive.OpenSubKey(Full(key.Key)))
        {
            if (opened is null || opened.SubKeyCount > 0 || opened.ValueCount > 0)
                return;
        }

        hive.DeleteSubKey(Full(key.Key), throwOnMissingSubKey: false);
    }
}
