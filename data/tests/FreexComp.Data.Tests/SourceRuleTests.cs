using System.Text.RegularExpressions;

namespace FreexComp.Data.Tests;

/// <summary>
/// Rules the Pascal sources of the data area keep (task A-077): the rejection beep hook belongs to the application and
/// its tests; a data unit reads it, and asks for a silent refusal through the framework's own switch instead of
/// replacing it.
/// </summary>
public sealed partial class SourceRuleTests
{
    [GeneratedRegex(@"\bRejectBeepHook\s*:=", RegexOptions.IgnoreCase)]
    private static partial Regex HookAssignment();

    [Fact]
    [Trait("Category", "Spec")]
    public void NoDataUnitAssignsTheRejectionBeepHook()
    {
        var units = Directory.GetFiles(DataCorpus.SourceDirectory, "*.pas", SearchOption.AllDirectories);
        Assert.NotEmpty(units);
        var offenders = units
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(x => HookAssignment().IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.file)}({x.number}): {x.line.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, "a data unit assigns RejectBeepHook:\n" + string.Join("\n", offenders));
    }
}
