using System.Text.RegularExpressions;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL-1066: in <c>rtl/src</c> the heap functions of the system are named only in <c>System.pas</c>, and there only in
/// their private <c>external</c> declarations and between the two marker comments of the default memory manager.
/// </summary>
public sealed partial class RtlUnitRulesTests
{
    private const string HeapBegin = "{ default memory manager: begin }";
    private const string HeapEnd = "{ default memory manager: end }";

    [GeneratedRegex(@"\{[^}]*\}|\(\*.*?\*\)|//[^\n]*|'[^'\n]*'", RegexOptions.Singleline)]
    private static partial Regex CommentOrString();

    [GeneratedRegex(@"\b(GetProcessHeap|HeapAlloc|HeapFree|HeapReAlloc|HeapSize)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HeapFunction();

    [GeneratedRegex(@"\bfunction\s+\w+\s*(\([^)]*\))?\s*:\s*\w+\s*;\s*stdcall\s*;\s*external\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalDeclaration();

    /// <summary>The uses of a heap function in <paramref name="text"/> that RTL-1066 does not allow, as <c>file(line): name</c>.</summary>
    public static List<string> StrayHeapFunctionUses(string fileName, string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var code = CommentOrString().Replace(text, m => new string(' ', m.Length));     // same offsets, no comments, no strings
        var allowed = new List<(int Start, int End)>();
        if (string.Equals(fileName, "System.pas", StringComparison.OrdinalIgnoreCase))
        {
            allowed.AddRange(ExternalDeclaration().Matches(code).Select(m => (m.Index, m.Index + m.Length)));
            var begin = text.IndexOf(HeapBegin, StringComparison.Ordinal);
            var end = text.IndexOf(HeapEnd, StringComparison.Ordinal);
            if (begin >= 0 && end > begin)
            {
                allowed.Add((begin, end));
            }
        }

        return
        [
            .. HeapFunction().Matches(code)
                .Where(m => !allowed.Any(a => m.Index >= a.Start && m.Index < a.End))
                .Select(m => $"{fileName}({text[..m.Index].Count(c => c == '\n') + 1}): {m.Value}"),
        ];
    }

    [Fact]
    [Trait("Req", "RTL-1066")]
    public void HeapFunctionsAreUsedOnlyByTheDefaultManager()
    {
        var files = Directory.GetFiles(SourceDirectory, "*.pas").Concat(Directory.GetFiles(SourceDirectory, "*.inc")).Order(StringComparer.Ordinal).ToList();
        Assert.Contains(files, f => Path.GetFileName(f) == "System.pas");
        var system = File.ReadAllText(Path.Combine(SourceDirectory, "System.pas"));
        Assert.Contains(HeapBegin, system, StringComparison.Ordinal);
        Assert.Contains(HeapEnd, system, StringComparison.Ordinal);
        Assert.True(HeapFunction().Matches(system).Count >= 8, "System.pas does not name the heap functions: the scanner sees nothing");

        var problems = files.SelectMany(f => StrayHeapFunctionUses(Path.GetFileName(f), File.ReadAllText(f))).ToList();
        Assert.True(problems.Count == 0, "heap functions outside the default manager:\n" + string.Join("\n", problems));
    }

    /// <summary>The scanner reports a stray call, a call in another unit and a call after the end marker, and nothing else.</summary>
    [Fact]
    [Trait("Req", "RTL-1066")]
    public void HeapFunctionsAreUsedOnlyByTheDefaultManager_ReportsStrayUses()
    {
        const string source = """
            unit System;
            implementation
            function HeapAlloc(hHeap: THandle; dwFlags: Cardinal; dwBytes: NativeUInt): Pointer; stdcall;
              external kernel32 name 'HeapAlloc';
            function GetProcessHeap: THandle; stdcall; external kernel32 name 'GetProcessHeap';
            { HeapFree is named in a comment } // and HeapSize here
            { default memory manager: begin }
            function SysGetMem(Size: NativeInt): Pointer;
            begin
              Result := HeapAlloc(ProcessHeap, 0, Size);
            end;
            { default memory manager: end }
            procedure Stray;
            begin
              P := HeapAlloc(GetProcessHeap, 0, 16);
            end;
            end.
            """;
        Assert.Equal(["System.pas(15): HeapAlloc", "System.pas(15): GetProcessHeap"], StrayHeapFunctionUses("System.pas", source));
        Assert.Equal(5, StrayHeapFunctionUses("Classes.pas", source).Count);     // another unit: declarations and marked code count too
        Assert.Empty(StrayHeapFunctionUses("System.pas", source.Replace("  P := HeapAlloc(GetProcessHeap, 0, 16);", "", StringComparison.Ordinal)));
    }
}
