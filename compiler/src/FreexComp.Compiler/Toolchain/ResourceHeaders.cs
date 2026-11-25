using System.Collections.Immutable;

namespace FreexComp.Compiler.Toolchain;

/// <summary>
/// CMP-1302, 1328: the product's own header files for resource scripts, carried as embedded resources and written to
/// <c>&lt;objdir&gt;\rc-include</c>. No header of another origin is used.
/// </summary>
internal static class ResourceHeaders
{
    public const string DirectoryName = "rc-include";

    private const string Prefix = "rc-include/";

    /// <summary>File name and content of every header, ordered by name.</summary>
    public static ImmutableArray<(string Name, byte[] Content)> Files { get; } = Load();

    /// <summary>Writes the headers below <paramref name="objectDirectory"/>; a file whose bytes already match is left alone.</summary>
    /// <returns>The include directory.</returns>
    public static string Write(IFileSystem fs, string objectDirectory)
    {
        var directory = Path.Combine(objectDirectory, DirectoryName);
        foreach (var (name, content) in Files)
        {
            var path = Path.Combine(directory, name);
            if (!fs.FileExists(path) || !fs.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            {
                fs.WriteAllBytes(path, content);
            }
        }

        return directory;
    }

    private static ImmutableArray<(string Name, byte[] Content)> Load()
    {
        var assembly = typeof(ResourceHeaders).Assembly;
        var files = ImmutableArray.CreateBuilder<(string Name, byte[] Content)>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            // LF line ends whatever the checkout wrote: the files are the same bytes on every machine
            files.Add((resource[Prefix.Length..], [.. memory.ToArray().Where(b => b != (byte)0x0D)]));
        }

        return files.ToImmutable();
    }
}
