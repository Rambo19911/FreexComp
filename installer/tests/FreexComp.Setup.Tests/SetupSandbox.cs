using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace FreexComp.Setup.Tests;

/// <summary>
/// One test's scratch world: a folder under %TEMP% (install target, Start menu, temp, profile folders) and the registry
/// root <c>HKCU\Software\FreexComp-SetupTest\&lt;guid&gt;</c>. Every setup run goes through the test switches into it, the
/// environment variables the setup reads point into it, and <see cref="Dispose"/> deletes both (also after a failure).
/// </summary>
internal sealed class SetupSandbox : IDisposable
{
    public const string TestRegistryParent = @"Software\FreexComp-SetupTest";

    public SetupSandbox()
    {
        var id = Guid.NewGuid().ToString("N");
        Root = Path.Combine(Path.GetTempPath(), "FreexComp-SetupTests", id);
        RegistryRoot = TestRegistryParent + @"\" + id;
        Directory.CreateDirectory(Root);
        foreach (var folder in (string[])[Temp, StartMenu, AppData, LocalAppData, ProgramFiles, ProgramData])
            Directory.CreateDirectory(folder);
    }

    public string Root { get; }

    /// <summary>The HKCU subkey of <c>/regroot=</c>.</summary>
    public string RegistryRoot { get; }

    public string Home => Path.Combine(Root, "Free Comp home");

    public string StartMenu => Path.Combine(Root, "start menu");

    public string Temp => Path.Combine(Root, "temp");

    public string AppData => Path.Combine(Root, "appdata");

    public string LocalAppData => Path.Combine(Root, "localappdata");

    public string ProgramFiles => Path.Combine(Root, "programfiles");

    public string ProgramData => Path.Combine(Root, "programdata");

    public string In(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>The environment variables of a setup run inside the sandbox (the real profile folders are never named).</summary>
    public Dictionary<string, string> Variables() => new(StringComparer.OrdinalIgnoreCase)
    {
        [CommandLine.TestVariable] = "1",
        ["TEMP"] = Temp,
        ["TMP"] = Temp,
        ["APPDATA"] = AppData,
        ["LOCALAPPDATA"] = LocalAppData,
        ["ProgramFiles"] = ProgramFiles,
        ["ProgramData"] = ProgramData,
        ["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows",
    };

    public SetupEnvironment EnvironmentFor(string program, string version = "1.0.0", IReadOnlyDictionary<string, string>? variables = null)
    {
        var map = variables ?? Variables();
        return new SetupEnvironment
        {
            ProgramPath = program,
            Variable = name => map.TryGetValue(name, out var value) ? value : null,
            Version = version,
            IsElevated = false,
            RunElevated = _ => throw new InvalidOperationException("a test never elevates"),
            NotifyShell = false,
        };
    }

    /// <summary>The switches that keep a run inside the sandbox, followed by <paramref name="more"/>.</summary>
    public string[] Switches(params string[] more) =>
        ["/S", "/D=" + Home, "/regroot=" + RegistryRoot, "/startmenu=" + StartMenu, .. more];

    /// <summary>Parses and runs a silent command line in-process.</summary>
    public SetupResult Run(string program, string[] args, string version = "1.0.0")
    {
        var environment = EnvironmentFor(program, version);
        return SetupProgram.Execute(CommandLine.Parse(args, environment.Variable), environment);
    }

    /// <summary>A run whose cancellation is already requested (the user pressed Cancel).</summary>
    public SetupResult RunCancelled(string program, string[] args)
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var environment = EnvironmentFor(program);
        return SetupProgram.Execute(CommandLine.Parse(args, environment.Variable), environment, cancel: cancelled.Token);
    }

    /// <summary>Writes a payload folder of text files (relative path → content) and returns its path.</summary>
    public string Payload(string name, IReadOnlyDictionary<string, string> files)
    {
        var folder = In("payloads", name);
        foreach (var (path, content) in files)
        {
            var file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content, new UTF8Encoding(false));
        }

        return folder;
    }

    /// <summary>A fake setup program (any bytes stand for the program in the in-process tests).</summary>
    public string FakeProgram()
    {
        var file = In("program.bin");
        File.WriteAllBytes(file, Encoding.ASCII.GetBytes("MZ fake setup program for the tests"));
        return file;
    }

    /// <summary>Builds a package of <paramref name="payload"/> (manifest written first) and returns its path.</summary>
    public string Package(string payload, string? program = null, string name = "setup.exe")
    {
        PackageWriter.WriteManifest(payload);
        return PackageAsIs(payload, program, name);
    }

    /// <summary>Builds a package of <paramref name="payload"/> with the manifest as it is on disk.</summary>
    public string PackageAsIs(string payload, string? program = null, string name = "setup.exe")
    {
        var output = In(name);
        PackageWriter.WriteSetup(program ?? FakeProgram(), payload, output);
        return output;
    }

    public RegistryKey? OpenKey(string relative) => Registry.CurrentUser.OpenSubKey(RegistryRoot + @"\" + relative);

    public object? Value(string relative, string name)
    {
        using var key = OpenKey(relative);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    /// <summary>Writes a value under the sandbox root (a foreign association the test expects to survive).</summary>
    public void SetValue(string relative, string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryRoot + @"\" + relative, writable: true);
        key.SetValue(name, value);
    }

    public bool RegistryRootExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRoot);
        return key is not null;
    }

    public void Dispose()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!process.ProcessName.StartsWith("FreexComp-uninstall-", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    if (process.MainModule?.FileName?.StartsWith(Root, StringComparison.OrdinalIgnoreCase) == true)
                        process.Kill();
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // gone meanwhile, or not ours
                }
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree(RegistryRoot, throwOnMissingSubKey: false);
        using (var parent = Registry.CurrentUser.OpenSubKey(TestRegistryParent))
        {
            if (parent is { SubKeyCount: 0, ValueCount: 0 })
            {
                try
                {
                    Registry.CurrentUser.DeleteSubKey(TestRegistryParent, throwOnMissingSubKey: false);
                }
                catch (InvalidOperationException)
                {
                    // another test created its root meanwhile
                }
            }
        }

        for (var attempt = 0; Directory.Exists(Root); attempt++)
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < 50)
            {
                Thread.Sleep(200);
            }
        }

        var parentFolder = Path.GetDirectoryName(Root)!;
        try
        {
            if (Directory.Exists(parentFolder) && !Directory.EnumerateFileSystemEntries(parentFolder).Any())
                Directory.Delete(parentFolder);
        }
        catch (IOException)
        {
            // another test's folder appeared meanwhile
        }
    }
}
