namespace FreexComp.Ide.Core.Composition;

/// <summary>
/// The parsed command line (phase1-shell.md §2, IDE-ARCH-015 step 1). Only <c>--user-dir</c> is interpreted so far;
/// every other argument is kept, in order, in <see cref="Arguments"/> for the command-line task.
/// </summary>
public sealed record StartupOptions(string? UserDir, IReadOnlyList<string> Arguments)
{
    public const string UserDirOption = "--user-dir";

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? userDir = null;
        var rest = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], UserDirOption, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                userDir = args[++i]; // IDE-P1-011: the last occurrence wins
            }
            else
            {
                rest.Add(args[i]);
            }
        }

        return new StartupOptions(userDir, rest);
    }
}
