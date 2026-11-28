using System.Collections.Immutable;
using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler;

/// <summary>
/// CMP-200 (C-452): open files win over the file system, also for the <c>{$I}</c> files a parse reads — an open include text (an
/// IDE buffer, the edited include text of a rename's fork) is what the including unit sees. Everything else goes to the file system.
/// </summary>
internal sealed class OpenFilesFileSystem(IFileSystem inner, ImmutableDictionary<string, SourceText> open) : IFileSystem
{
    public bool FileExists(string path) => open.ContainsKey(PathUtil.Normalize(path)) || inner.FileExists(path);

    public string ReadAllText(string path) => open.TryGetValue(PathUtil.Normalize(path), out var text) ? text.Text : inner.ReadAllText(path);

    public byte[] ReadAllBytes(string path) =>
        open.TryGetValue(PathUtil.Normalize(path), out var text) ? Encoding.UTF8.GetBytes(text.Text) : inner.ReadAllBytes(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) => inner.EnumerateFiles(directory, searchPattern);

    public IEnumerable<string> EnumerateDirectories(string directory) => inner.EnumerateDirectories(directory);

    public DateTime GetLastWriteTimeUtc(string path) => inner.GetLastWriteTimeUtc(path);

    public long GetLength(string path) => inner.GetLength(path);

    public void WriteAllBytes(string path, byte[] data) => inner.WriteAllBytes(path, data);

    public void Delete(string path) => inner.Delete(path);

    public void CreateDirectory(string path) => inner.CreateDirectory(path);

    public void DeleteDirectory(string path) => inner.DeleteDirectory(path);
}
