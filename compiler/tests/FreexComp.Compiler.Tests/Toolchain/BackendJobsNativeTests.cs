using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>
/// F01-010, C-275 (parallel-backend.md CMP-1144): on the real toolchain the job limit changes no output byte. A fixture of
/// 12 units (the program and 11 units with interface dependencies between them, one of them importing from a DLL other than
/// <c>kernel32</c>) is built at <c>-O2</c> with <c>J = 1</c> and with <c>J = 8</c> in the same directory, cleaned in between;
/// the SHA-256 of every <c>.ll</c>, <c>.obj</c>, <c>.pcu</c>, <c>imports\*.lib</c>, of <c>link.rsp</c> and of the linked image
/// is compared, and so is the text of the diagnostics.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class BackendJobsNativeTests
{
    private const int Units = 11;

    private static string UnitName(int i) => "Job" + i.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>Job01</c> declares a record and an external of <c>user32.dll</c>; every later unit uses its predecessor in the interface
    /// and calls it and <c>Job01</c>'s external in its body, so the units differ and depend on each other.
    /// </summary>
    private static string UnitSource(int i)
    {
        var sb = new StringBuilder();
        sb.Append("unit ").Append(UnitName(i)).Append(";\n\ninterface\n\n");
        if (i == 1)
        {
            sb.Append("type\n  TAcc = record\n    Sum: Integer;\n    Count: Integer;\n  end;\n\n");
            sb.Append("function ScreenMetric(Index: Integer): Integer; stdcall; external 'user32.dll' name 'GetSystemMetrics';\n");
            sb.Append("procedure Step01(var A: TAcc; V: Integer);\n\nimplementation\n\n");
            sb.Append("procedure Step01(var A: TAcc; V: Integer);\nbegin\n  A.Sum := A.Sum + V;\n  A.Count := A.Count + 1;\nend;\n\nend.\n");
            return sb.ToString();
        }

        var n = i.ToString("00", CultureInfo.InvariantCulture);
        var previous = (i - 1).ToString("00", CultureInfo.InvariantCulture);
        sb.Append("uses ").Append(i == 2 ? "Job01" : "Job01, Job" + previous).Append(";\n\n");
        sb.Append("procedure Step").Append(n).Append("(var A: TAcc; V: Integer);\n");
        sb.Append("function Scale").Append(n).Append("(const A: TAcc): Integer;\n\nimplementation\n\n");
        sb.Append("procedure Step").Append(n).Append("(var A: TAcc; V: Integer);\nvar\n  K: Integer;\nbegin\n");
        sb.Append("  for K := 1 to ").Append(i).Append(" do\n    Step").Append(previous).Append("(A, V * K + ").Append(i * 7).Append(");\n");
        sb.Append("  if ScreenMetric(").Append(i).Append(") < 0 then\n    A.Sum := A.Sum + ").Append(i).Append(";\nend;\n\n");
        sb.Append("function Scale").Append(n).Append("(const A: TAcc): Integer;\nbegin\n  Result := A.Sum * ").Append(i).Append(" + A.Count;\nend;\n\nend.\n");
        return sb.ToString();
    }

    private static string ProgramSource()
    {
        var sb = new StringBuilder("program JobsDemo;\n\nuses\n  ");
        sb.AppendJoin(", ", Enumerable.Range(1, Units).Select(UnitName)).Append(";\n\nvar\n  A: TAcc;\nbegin\n  A.Sum := 0;\n  A.Count := 0;\n");
        for (var i = 2; i <= Units; i++)
        {
            var n = i.ToString("00", CultureInfo.InvariantCulture);
            sb.Append("  Step").Append(n).Append("(A, ").Append(i).Append(");\n  WriteLn(Scale").Append(n).Append("(A));\n");
        }

        return sb.Append("end.\n").ToString();
    }

    /// <summary>One build with the given job limit; file name (relative to the directory) → SHA-256, plus the diagnostics text.</summary>
    private static (SortedDictionary<string, string> Hashes, string Diagnostics) Build(string directory, int jobs)
    {
        var obj = Path.Combine(directory, "obj");
        var exe = Path.Combine(directory, "out", "JobsDemo.exe");
        var options = new CompilationOptions
        {
            MainFile = PathUtil.Normalize(Path.Combine(directory, "JobsDemo.pas")),
            RtlDirectory = TestRtl.Directory,
            ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
            ObjectDirectory = obj,
            Optimization = OptimizationLevel.O2,
            MaxParallelism = jobs,
        };
        var result = Compilation.Create(options, [], PhysicalFileSystem.Instance).Emit(new EmitOptions { OutputPath = exe }, TestContext.Current.CancellationToken);
        var diagnostics = string.Join("\n", result.Diagnostics.Select(d => d.ToText()));
        Assert.True(result.Success, "J = " + jobs + ":\n" + diagnostics);
        Assert.Equal(Units + 2, result.CompiledUnits.Length);                       // the program, its 11 units, System

        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(obj, "*", SearchOption.AllDirectories)
                     .Where(f => Path.GetExtension(f) is ".ll" or ".obj" or ".pcu" or ".lib" or ".rsp")
                     .Append(exe))
        {
            hashes.Add(Path.GetRelativePath(directory, file), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        }

        return (hashes, diagnostics);
    }

    [Fact]
    [Trait("Req", "CMP-1144")]
    public void ObjectsByteIdenticalToSerial()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "JobsDemo.pas"), ProgramSource());
            for (var i = 1; i <= Units; i++)
            {
                File.WriteAllText(Path.Combine(dir, UnitName(i) + ".pas"), UnitSource(i));
            }

            var serial = Build(dir, jobs: 1);
            TestDirectories.Delete(Path.Combine(dir, "obj"));                       // cleaned in between: nothing is up to date
            TestDirectories.Delete(Path.Combine(dir, "out"));
            var parallel = Build(dir, jobs: 8);

            // every kind of output is there: 13 .ll / .obj / .pcu, the import libraries (user32 among them), link.rsp, the image
            foreach (var extension in new[] { ".ll", ".obj", ".pcu" })
            {
                Assert.Equal(Units + 2, serial.Hashes.Keys.Count(f => f.EndsWith(extension, StringComparison.Ordinal)));
            }

            Assert.Contains(Path.Combine("obj", "imports", "user32.lib"), serial.Hashes.Keys);
            Assert.Contains(Path.Combine("obj", "imports", "kernel32.lib"), serial.Hashes.Keys);
            Assert.Contains(Path.Combine("obj", "link.rsp"), serial.Hashes.Keys);
            Assert.Contains(Path.Combine("out", "JobsDemo.exe"), serial.Hashes.Keys);

            Assert.Equal(serial.Hashes.Keys, parallel.Hashes.Keys);
            var differing = serial.Hashes.Keys.Where(f => serial.Hashes[f] != parallel.Hashes[f]).ToList();
            Assert.True(differing.Count == 0, "J = 8 differs from J = 1 in: " + string.Join(", ", differing));
            Assert.Equal(serial.Diagnostics, parallel.Diagnostics);
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }
}
