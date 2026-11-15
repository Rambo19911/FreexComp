using System.Text;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>
/// Project file access through <c>FreexComp.Build.ProjectSerializer</c> (project-manager.md IDE-PM-001/003/005/011/012):
/// the IDE file system reaches the serializer through the adapter, and load diagnostics become <c>Msg.Project.*</c>.
/// </summary>
[Trait("Phase", "1")]
public sealed class ProjectFileTests
{
    private const string ProjectPath = @"C:\p\Hello\Hello.pproj";

    /// <summary>The canonical layout of project-format.md §1 (2-space indent, LF, final newline).</summary>
    internal const string Canonical = """
        {
          "formatVersion": 1,
          "name": "Hello",
          "language": "pascal",
          "appType": "console",
          "mainSource": "Hello.pas",
          "files": [
            {
              "path": "Hello.pas"
            },
            {
              "path": "MainForm.pas",
              "form": "MainForm.form"
            }
          ],
          "searchPaths": [],
          "defines": [],
          "platforms": [
            "win64"
          ],
          "configurations": {
            "Debug": {
              "optimization": 0,
              "debugInfo": true,
              "defines": [
                "DEBUG"
              ],
              "outputDir": "bin/Debug"
            },
            "Release": {
              "optimization": 2,
              "debugInfo": false,
              "defines": [
                "RELEASE"
              ],
              "outputDir": "bin/Release"
            }
          },
          "activeConfiguration": "Debug",
          "run": {
            "arguments": "",
            "workingDir": ""
          },
          "events": {
            "preBuild": [],
            "postBuild": []
          },
          "dependencies": []
        }

        """;

    private readonly InMemoryFileSystem _fs = new();

    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private void Write(string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(ProjectPath)!);
        _fs.WriteAllBytes(ProjectPath, Encoding.UTF8.GetBytes(Lf(text)));
    }

    private byte[] Disk() => _fs.ReadAllBytes(ProjectPath);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-003")]
    public void RoundTripCanonical()
    {
        Write(Canonical);
        var before = Disk();

        var load = ProjectFileAccess.Load(ProjectPath, _fs);
        Assert.True(load.Success, load.Error.MessageId);
        Assert.Equal("Hello", load.Project!.Name);
        Assert.Equal(ProjectPath, load.Project.FilePath);

        var save = ProjectFileAccess.Save(load.Project, _fs);

        Assert.True(save.Success, save.MessageId);
        Assert.Equal(before, Disk());
        Assert.False(_fs.ReadAllBytes(ProjectPath).AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]));
        Assert.Single(_fs.EnumerateFiles(Path.GetDirectoryName(ProjectPath)!, "*"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-003")]
    public void PreservesUnknownKeys()
    {
        // Non-canonical: BOM, CRLF, 4-space indent, compact arrays, unknown keys at top level and in a file entry.
        var input = "\uFEFF{\r\n    \"x-before\": 1,\r\n    \"formatVersion\": 1, \"name\": \"Hello\",\r\n"
            + "    \"mainSource\": \"Hello.pas\", \"x-tool\": { \"a\": [1, 2] },\r\n"
            + "    \"files\": [ { \"path\": \"Hello.pas\", \"x-note\": \"keep\" } ],\r\n"
            + "    \"x-last\": true\r\n}\r\n";
        _fs.CreateDirectory(Path.GetDirectoryName(ProjectPath)!);
        _fs.WriteAllBytes(ProjectPath, Encoding.UTF8.GetBytes(input));

        var load = ProjectFileAccess.Load(ProjectPath, _fs);
        Assert.True(load.Success, load.Error.MessageId);
        var save = ProjectFileAccess.Save(load.Project! with { ActiveConfiguration = "Release" }, _fs);
        Assert.True(save.Success, save.MessageId);

        var expected = Lf("""
            {
              "x-before": 1,
              "formatVersion": 1,
              "name": "Hello",
              "language": "pascal",
              "appType": "console",
              "mainSource": "Hello.pas",
              "x-tool": {
                "a": [
                  1,
                  2
                ]
              },
              "files": [
                {
                  "path": "Hello.pas",
                  "x-note": "keep"
                }
              ],
              "searchPaths": [],
              "defines": [],
              "platforms": [
                "win64"
              ],
              "configurations": {
                "Debug": {
                  "optimization": 0,
                  "debugInfo": true,
                  "defines": [
                    "DEBUG"
                  ],
                  "outputDir": "bin/Debug"
                },
                "Release": {
                  "optimization": 2,
                  "debugInfo": false,
                  "defines": [
                    "RELEASE"
                  ],
                  "outputDir": "bin/Release"
                }
              },
              "activeConfiguration": "Release",
              "run": {
                "arguments": "",
                "workingDir": ""
              },
              "events": {
                "preBuild": [],
                "postBuild": []
              },
              "dependencies": [],
              "x-last": true
            }

            """);
        Assert.Equal(expected, Encoding.UTF8.GetString(Disk()));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-005")]
    public void RejectsNewerFormatVersion()
    {
        var text = Canonical.Replace("\"formatVersion\": 1", "\"formatVersion\": 7", StringComparison.Ordinal);
        Write(text);
        var before = Disk();

        var load = ProjectFileAccess.Load(ProjectPath, _fs);

        Assert.False(load.Success);
        Assert.Null(load.Project);
        Assert.Equal("Msg.Project.NewerFormat", load.Error.MessageId);
        Assert.Equal(["7", "1"], load.Error.Args);
        Assert.Equal(before, Disk());
    }

    [Theory(Timeout = 30000)]
    [InlineData("{\n  \"formatVersion\": 1,\n  \"name\": 5,\n  \"mainSource\": \"Hello.pas\"\n}\n", 3, 11)]
    [InlineData("{\n  \"formatVersion\": 1,\n  // a comment\n  \"name\": \"Hello\",\n  \"mainSource\": \"Hello.pas\"\n}\n", 3, 3)]
    [InlineData("{\n  \"formatVersion\": 1,\n  \"name\": \"Hello\"\n}\n", 1, 1)]
    [Trait("Req", "IDE-PM-011")]
    [Trait("Req", "IDE-PM-012")]
    public void ValidationErrorsCarryPosition(string text, int line, int column)
    {
        Write(text);

        var load = ProjectFileAccess.Load(ProjectPath, _fs);

        Assert.False(load.Success);
        Assert.Equal("Msg.Project.OpenFailed", load.Error.MessageId);
        Assert.Equal(ProjectPath, load.Error.Args[0]);
        var diagnostic = Assert.IsType<FreexComp.Compiler.Diagnostic>(load.Diagnostic);
        Assert.Equal("E9002", diagnostic.Code);
        Assert.Equal((line, column), load.Position);
        Assert.StartsWith($"Hello.pproj({line},{column}): ", load.Error.Args[1], StringComparison.Ordinal);
        Assert.Contains(diagnostic.Message, load.Error.Args[1], StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-011")]
    public void MissingFileGivesOsReason()
    {
        var missing = ProjectFileAccess.Load(ProjectPath, _fs);

        Assert.Equal("Msg.Project.OpenFailed", missing.Error.MessageId);
        Assert.Equal(ProjectPath, missing.Error.Args[0]);
        Assert.Equal(missing.Diagnostic!.Message, missing.Error.Args[1]);
        Assert.Null(missing.Position);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-001")]
    public void CompilerAdapterReadsTextWithoutBomAndCreatesDirectories()
    {
        var compilerFs = new CompilerFileSystemAdapter(_fs);
        _fs.CreateDirectory(@"C:\a");
        _fs.WriteAllBytes(@"C:\a\x.txt", [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i']);

        Assert.Equal("hi", compilerFs.ReadAllText(@"C:\a\x.txt"));
        Assert.Throws<FileNotFoundException>(() => compilerFs.ReadAllText(@"C:\a\none.txt"));
        Assert.Empty(compilerFs.EnumerateFiles(@"C:\missing", "*"));

        compilerFs.WriteAllBytes(@"C:\b\c\y.bin", [1, 2]);
        Assert.Equal([1, 2], _fs.ReadAllBytes(@"C:\b\c\y.bin"));
        Assert.Equal([@"C:\b\c\y.bin"], compilerFs.EnumerateFiles(@"C:\b\c", "*.bin"));

        compilerFs.Delete(@"C:\a\none.txt");
        compilerFs.Delete(@"C:\a\x.txt");
        Assert.False(compilerFs.FileExists(@"C:\a\x.txt"));
    }

    /// <summary>The compiler deletes a stale unit file before its first build, when its output folder does not exist yet (I-128).</summary>
    [Fact(Timeout = 30000)]
    [Trait("Category", "Integration")]
    [Trait("Req", "IDE-PM-001")]
    public void CompilerAdapterDeleteInMissingFolderIsNoOp()
    {
        using var temp = new TempDirectory();
        var compilerFs = new CompilerFileSystemAdapter(new Core.Platform.PhysicalFileSystem());

        compilerFs.Delete(temp.Combine(@"obj\Debug\Hello.pcu"));

        Assert.False(Directory.Exists(temp.Combine("obj")));
    }
}
