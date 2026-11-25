using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>
/// What <see cref="FakeToolchain"/> does for the two tools of a resource script: a preprocessor that expands
/// <c>#include "file"</c> lines (with line markers) and drops other directive lines, and a resource tool that writes a valid
/// compiled resource file whose single entry holds its input and the content of every data file the input names.
/// </summary>
internal static partial class FakeResourceTools
{
    /// <summary>A script containing this text makes the fake resource tool fail.</summary>
    public const string SyntaxError = "SYNTAX ERROR";

    private static readonly byte[] EmptyEntry =
        [0, 0, 0, 0, 0x20, 0, 0, 0, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>A compiled resource file with one entry per element (numeric type and name, language 0x0409).</summary>
    public static byte[] File(params (int Type, int Name, byte[] Data)[] entries)
    {
        var file = new List<byte>(EmptyEntry);
        foreach (var (type, name, data) in entries)
        {
            file.AddRange(BitConverter.GetBytes(data.Length));
            file.AddRange(BitConverter.GetBytes(32));
            file.AddRange([0xFF, 0xFF, (byte)type, (byte)(type >> 8), 0xFF, 0xFF, (byte)name, (byte)(name >> 8)]);
            file.AddRange([0, 0, 0, 0, 0x30, 0, 0x09, 0x04, 0, 0, 0, 0, 0, 0, 0, 0]);
            file.AddRange(data);
            while (file.Count % 4 != 0)
            {
                file.Add(0);
            }
        }

        return [.. file];
    }

    public static string Preprocess(IFileSystem fs, IReadOnlyList<string> arguments)
    {
        var script = arguments[^1];
        var directories = new List<string>();
        for (var i = 0; i < arguments.Count - 1; i++)
        {
            if (arguments[i] is "-I" or "-isystem")
            {
                directories.Add(arguments[i + 1]);
            }
        }

        var text = new StringBuilder();
        Expand(script);
        return text.ToString();

        void Expand(string path)
        {
            text.Append(Marker(1, path));
            var lines = fs.ReadAllText(path).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (Include().Match(line) is { Success: true } include)
                {
                    var name = include.Groups[1].Value;
                    var found = directories.Prepend(Path.GetDirectoryName(path)!).Select(d => Path.Combine(d, name)).First(fs.FileExists);
                    Expand(found);
                    text.Append(Marker(i + 2, path));
                }
                else if (!line.StartsWith('#'))
                {
                    text.Append(line).Append('\n');
                }
            }
        }

        static string Marker(int line, string path) => $"# {line} \"{path.Replace(@"\", @"\\", StringComparison.Ordinal)}\"\n";
    }

    public static (byte[] Output, string? Error) Compile(IFileSystem fs, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var input = fs.ReadAllText(arguments[^1]);
        if (input.Contains(SyntaxError, StringComparison.Ordinal))
        {
            return ([0], "llvm-rc: Error parsing file: expected int or string, got SYNTAX");
        }

        var directories = new List<string> { workingDirectory };
        for (var i = 0; i < arguments.Count - 1; i++)
        {
            if (arguments[i] == "/I")
            {
                directories.Add(arguments[i + 1]);
            }
        }

        var data = new List<byte>(Encoding.UTF8.GetBytes(input));
        foreach (Match name in Quoted().Matches(input))
        {
            if (directories.Select(d => Path.Combine(d, name.Groups[1].Value)).FirstOrDefault(fs.FileExists) is { } file)
            {
                data.AddRange(fs.ReadAllBytes(file));
            }
        }

        return (File((10, 1, [.. data])), null);
    }

    [GeneratedRegex(@"^\s*#include\s+""([^""]+)""")]
    private static partial Regex Include();

    [GeneratedRegex(@"""([^""]+)""")]
    private static partial Regex Quoted();
}
