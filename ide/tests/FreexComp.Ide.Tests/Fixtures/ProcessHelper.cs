using FreexComp.Ide.TestHelper;

namespace FreexComp.Ide.Tests.Fixtures;

/// <summary>
/// IDE-P3-094, I-194: how the process tests start their helper program, <see cref="HelperProgram"/>: its own apphost exe,
/// copied next to the test assembly by the project reference (no dotnet host, no test framework in the child).
/// </summary>
internal static class ProcessHelper
{
    /// <summary>The helper executable and the arguments for <paramref name="mode"/> (a mode constant of <see cref="HelperProgram"/> plus its operands).</summary>
    public static (string FileName, string Arguments) Command(string mode) => (HelperProgram.ExecutablePath, mode);
}
