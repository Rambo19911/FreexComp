using System.Text;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;
using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>
/// C-167 (toolchain.md CMP-628, CMP-629 variant 1): a real link with the pinned <c>lld-link</c>, the exe's <c>RT_MANIFEST</c>
/// resource 1 read back by walking the PE resource directory. <c>lld-link /manifestinput</c> runs its built-in manifest merger,
/// which re-serialises the XML (it drops <c>standalone</c> and puts its own <c>trustInfo</c> first), so the resource is compared
/// with <c>app.manifest</c> as XML: the same elements, attributes and text, in any sibling order.
/// C-295 (app-manifest.md CMP-1100…1106): console programs get a generated manifest too; execution level; long-path awareness.
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
        Assert.Equal(new UTF8Encoding(false).GetBytes(ApplicationManifest.Generate(AppType.Gui)), File.ReadAllBytes(manifest));
        AssertSameXml(File.ReadAllText(manifest), ManifestResource(Exe));
        Assert.Contains("/manifestinput:" + manifest, File.ReadAllLines(Path.Combine(Obj, "link.rsp")));
    }

    /// <summary>
    /// CMP-1100 (replaces <c>ConsoleProgram_HasNoManifest</c>, whose rule — CMP-628 "console: none" — is replaced): a console
    /// program embeds the generated console manifest as resource 1.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1100")]
    public void ConsoleProgram_EmbedsGeneratedManifest()
    {
        Build(AppType.Console, "");

        var manifest = Path.Combine(Obj, "app.manifest");
        Assert.Equal(File.ReadAllBytes(ApplicationManifestTests.GoldenPath("app.console.manifest")), File.ReadAllBytes(manifest));
        AssertSameXml(File.ReadAllText(ApplicationManifestTests.GoldenPath("app.console.manifest")), ManifestResource(Exe));
        Assert.Contains("/manifestinput:" + manifest, File.ReadAllLines(Path.Combine(Obj, "link.rsp")));
    }

    /// <summary>Card acceptance 1: the resource of a GUI program, read back from the exe, is the golden text as XML.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1102")]
    public void GuiProgram_EmbeddedManifestXmlEqualsGolden()
    {
        Build(AppType.Gui, "");

        AssertSameXml(File.ReadAllText(ApplicationManifestTests.GoldenPath("app.gui.manifest")), ManifestResource(Exe));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1100")]
    public void ManifestNone_ConsoleProgramHasNoManifest()
    {
        Build(AppType.Console, "none");

        Assert.Null(ManifestResource(Exe));
        Assert.False(File.Exists(Path.Combine(Obj, "app.manifest")));
        Assert.DoesNotContain(File.ReadAllLines(Path.Combine(Obj, "link.rsp")), l => l.StartsWith("/manifest", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1100")]
    public void LibraryStillHasNoManifestByDefault()
    {
        PhysicalFileSystem.Instance.WriteAllBytes(Path.Combine(_dir, "L.pas"), Encoding.UTF8.GetBytes(
            "library L;\nfunction Add(A, B: Integer): Integer; stdcall;\nbegin\n  Result := A + B;\nend;\nexports Add;\nbegin\nend.\n"));
        var dll = Path.Combine(_dir, "L.dll");

        Build(AppType.Library, "", main: "L.pas", output: dll);

        Assert.Null(ManifestResource(dll, 1));
        Assert.Null(ManifestResource(dll, 2));
        Assert.False(File.Exists(Path.Combine(Obj, "app.manifest")));
        Assert.DoesNotContain(File.ReadAllLines(Path.Combine(Obj, "link.rsp")), l => l.StartsWith("/manifest", StringComparison.Ordinal));
    }

    /// <summary>CMP-1106: the execution level is no part of a unit's options fingerprint — a change runs only the linker.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1106")]
    [Trait("Req", "CMP-155")]
    public void ExecutionLevelChange_RelinksWithoutRecompilingUnits()
    {
        static bool IsLink(string command) => command.Contains("lld-link", StringComparison.OrdinalIgnoreCase);
        var first = Build(AppType.Console, "");
        Assert.Contains(first, c => !IsLink(c));                             // the first build compiles
        Assert.Contains("level=\"asInvoker\"", ManifestResource(Exe), StringComparison.Ordinal);

        Assert.Empty(Build(AppType.Console, ""));                            // unchanged: nothing runs

        var second = Build(AppType.Console, "", level: ManifestExecutionLevel.HighestAvailable);
        Assert.NotEmpty(second);
        Assert.All(second, c => Assert.True(IsLink(c), c));
        Assert.Contains("level=\"highestAvailable\"", ManifestResource(Exe), StringComparison.Ordinal);

        var third = Build(AppType.Console, "", level: ManifestExecutionLevel.HighestAvailable, dpi: ManifestDpiAwareness.System);
        Assert.NotEmpty(third);
        Assert.All(third, c => Assert.True(IsLink(c), c));
        Assert.Contains(">System<", ManifestResource(Exe), StringComparison.Ordinal);
    }

    /// <summary>
    /// CMP-1103: <c>longPathAware</c> reaches the loader. The program creates nested directories with <c>CreateDirectoryW</c>
    /// (no <c>\\?\</c> prefix) until the path has at least 300 characters; that succeeds exactly when the system-wide long-path
    /// policy is on and the image declares the awareness.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1103")]
    public async Task LongPath_BehaviourMatchesSystemPolicy()
    {
        Assert.True(OperatingSystem.IsWindows());
        var policyOn = OperatingSystem.IsWindows()
            && Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 0) is 1;
        PhysicalFileSystem.Instance.WriteAllBytes(Path.Combine(_dir, "LongPath.pas"), Encoding.UTF8.GetBytes(LongPathSource));

        async Task<string> RunAsync(string manifest, string folder)
        {
            var exe = Path.Combine(_dir, folder, "LongPath.exe");
            var root = Path.Combine(_dir, folder, "tree");
            Directory.CreateDirectory(root);
            Build(AppType.Console, manifest, main: "LongPath.pas", output: exe, objectDirectory: Path.Combine(_dir, folder, "obj"));
            var lines = new List<string>();
            try
            {
                var code = await NativeRun.RunAsync(exe, [root], _dir, lines.Add, lines.Add);
                Assert.Equal(0, code);
                var deepest = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Select(d => d.Length).DefaultIfEmpty(0).Max();
                Assert.Equal(lines.Single() == "created", deepest >= 300);
                return lines.Single();
            }
            finally
            {
                Directory.Delete(root, recursive: true);                     // the test removes what the program created
            }
        }

        Assert.Equal(policyOn ? "created" : "refused", await RunAsync("", "aware"));
        Assert.Equal("refused", await RunAsync("none", "plain"));
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

    private const string LongPathSource = """
        program LongPath;

        function CreateDirectoryW(Name: PWideChar; Security: Pointer): LongBool; stdcall; external 'kernel32.dll' name 'CreateDirectoryW';

        var
          Path: string;
          Ok: Boolean;
        begin
          Path := ParamStr(1);
          Ok := True;
          while Ok and (Length(Path) < 300) do
          begin
            Path := Path + '\d123456789012345678901234567890123456789';
            Ok := CreateDirectoryW(PWideChar(Path), nil);
          end;
          if Ok then
            Writeln('created')
          else
            Writeln('refused');
        end.

        """;

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

    private List<string> Build(AppType appType, string manifest, ManifestExecutionLevel level = ManifestExecutionLevel.AsInvoker,
        ManifestDpiAwareness dpi = ManifestDpiAwareness.Default, string main = "P.pas", string? output = null, string? objectDirectory = null)
    {
        var commands = new List<string>();
        var options = new CompilationOptions
        {
            MainFile = Path.Combine(_dir, main),
            RtlDirectory = TestRtl.Directory,
            ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
            ObjectDirectory = objectDirectory ?? Obj,
            AppType = appType,
            Manifest = manifest,
            ManifestExecutionLevel = level,
            ManifestDpiAwareness = dpi,
        };

        var result = Compilation.Create(options, [], PhysicalFileSystem.Instance)
            .Emit(new EmitOptions { OutputPath = output ?? Exe, CommandLineSink = commands.Add }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "W4003");
        return commands;
    }

    private static void AssertSameXml(string expected, string? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(PeManifest.Canonical(expected), PeManifest.Canonical(actual));
    }

    private static string? ManifestResource(string image, uint id = 1) => PeManifest.Read(image, id);
}
