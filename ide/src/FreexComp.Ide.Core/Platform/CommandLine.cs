using System.Text;

namespace FreexComp.Ide.Core.Platform;

/// <summary>Command-line text by the system's documented argument rules (IDE-P3-099).</summary>
public static class CommandLine
{
    /// <summary>
    /// IDE-P3-099: a command-line tail of <paramref name="arguments"/>, the inverse of
    /// <see cref="Debugging.DebugService.SplitArguments"/>. Arguments are separated by one space; one that is not empty and
    /// has no space, tab or <c>"</c> is written as it is; any other is enclosed in <c>"</c>, each <c>"</c> inside gets a
    /// backslash in front, and a run of backslashes directly before a <c>"</c> (inside, or the closing one) is doubled.
    /// </summary>
    public static string Join(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var text = new StringBuilder();
        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument, nameof(arguments));
            if (text.Length > 0)
                text.Append(' ');
            if (argument.Length > 0 && argument.AsSpan().IndexOfAny(" \t\"") < 0)
            {
                text.Append(argument);
                continue;
            }

            text.Append('"');
            var backslashes = 0;
            foreach (var c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                text.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
                text.Append(c);
                backslashes = 0;
            }

            text.Append('\\', backslashes * 2).Append('"');
        }

        return text.ToString();
    }
}
