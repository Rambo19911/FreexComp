using System.Globalization;
using System.Text.RegularExpressions;

namespace FreexComp.Debugger.Session;

/// <summary>
/// The rules of exception notification that need no debuggee (features §6): the ignore list, the code of an
/// operating-system exception stop and the text table of the codes. The session does the reading and the resuming.
/// </summary>
internal static partial class ExceptionMonitor
{
    /// <summary>The thread-naming convention: never a stop (DBG-249).</summary>
    public const uint ThreadNameCode = 0x406D1388;

    /// <summary>A breakpoint instruction in the program's code (DBG-249).</summary>
    public const uint BreakpointCode = 0x80000003;

    /// <summary>
    /// DBG-247: the first token "0x" + exactly 8 hexadecimal digits of the adapter's description, else of its text;
    /// 0 when neither has one (an address has more digits and is not a code).
    /// </summary>
    public static uint ParseCode(string? description, string? text)
    {
        foreach (var source in (string?[])[description, text])
        {
            if (source is not null && CodeToken().Match(source) is { Success: true } match)
                return uint.Parse(match.Groups[1].ValueSpan, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        return 0;
    }

    [GeneratedRegex("(?<![0-9A-Za-z])0[xX]([0-9A-Fa-f]{8})(?![0-9A-Fa-f])", RegexOptions.CultureInvariant)]
    private static partial Regex CodeToken();

    /// <summary>The text of an operating-system exception code (DBG-249).</summary>
    public static string OsText(uint code) => code switch
    {
        0xC0000005 => "Access violation",
        0xC0000094 => "Integer division by zero",
        0xC0000095 => "Integer overflow",
        0xC000008C => "Array bounds exceeded",
        0xC000008E => "Floating-point division by zero",
        0xC0000090 => "Invalid floating-point operation",
        0xC0000091 => "Floating-point overflow",
        0xC0000093 => "Floating-point underflow",
        0xC00000FD => "Stack overflow",
        0xC000001D => "Illegal instruction",
        0xC0000096 => "Privileged instruction",
        _ => Messages.UnknownOsException(code),
    };

    /// <summary>
    /// DBG-244: an entry of the list equals (case-insensitive) the name of the exception's dynamic class or of one of
    /// its ancestors. <paramref name="classes"/> is the class followed by its ancestors as far as they are known; with
    /// the class alone this is the exact match. A name that could not be read ("") matches nothing.
    /// </summary>
    public static bool IsIgnored(ExceptionSettings settings, IReadOnlyList<string> classes) =>
        classes.Any(name => name.Length > 0
            && settings.IgnoredClasses.Any(entry => string.Equals(entry, name, StringComparison.OrdinalIgnoreCase)));
}
