using System.Reflection.PortableExecutable;

namespace FreexComp.Ide.Tests.Platform;

/// <summary>Backlog card F11-001: the IDE runs as a 64-bit process (ADR 002, ADR 007).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class ProcessBitnessTests
{
    private static string IdeAssemblyPath => typeof(App).Assembly.Location;

    /// <summary>The IDE assembly, loaded in this process, runs with 64-bit pointers.</summary>
    [Fact]
    public void IdeCodeRunsIn64BitProcess()
    {
        Assert.True(File.Exists(IdeAssemblyPath));
        Assert.True(Environment.Is64BitProcess);
        Assert.Equal(8, IntPtr.Size);
    }

    /// <summary>The IDE's launcher executable, built next to the assembly, is an x64 image: it starts a 64-bit process.</summary>
    [Fact]
    public void IdeExecutableIsX64Image()
    {
        var exe = Path.ChangeExtension(IdeAssemblyPath, ".exe");
        Assert.True(File.Exists(exe), "no IDE executable: " + exe);
        using var pe = new PEReader(File.OpenRead(exe));
        Assert.Equal(Machine.Amd64, pe.PEHeaders.CoffHeader.Machine);
        Assert.Equal(PEMagic.PE32Plus, pe.PEHeaders.PEHeader!.Magic);
    }

    /// <summary>The IDE assembly neither requires nor prefers a 32-bit process.</summary>
    [Fact]
    public void IdeAssemblyDoesNotAskFor32Bit()
    {
        using var pe = new PEReader(File.OpenRead(IdeAssemblyPath));
        var flags = pe.PEHeaders.CorHeader!.Flags;
        Assert.Equal((CorFlags)0, flags & (CorFlags.Requires32Bit | CorFlags.Prefers32Bit));
    }
}
