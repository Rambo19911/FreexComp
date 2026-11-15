using System.Text;

namespace FreexComp.Build;

/// <summary>Splits a command-line argument string with the Windows (C runtime) rules (build-engine.md CMP-833).</summary>
internal static class CommandLineArguments
{
    /// <summary>
    /// Whitespace separates arguments outside quotes; <c>"</c> toggles quoting; inside quotes <c>""</c> is a literal quote;
    /// 2n backslashes before a quote give n backslashes (the quote is special), 2n+1 give n backslashes and a literal quote;
    /// other backslashes are literal.
    /// </summary>
    public static IReadOnlyList<string> Split(string text)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inArgument = false;
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!quoted && c is ' ' or '\t')
            {
                if (inArgument)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    inArgument = false;
                }

                continue;
            }

            inArgument = true;
            if (c == '\\')
            {
                var start = i;
                while (i < text.Length && text[i] == '\\')
                {
                    i++;
                }

                var count = i - start;
                if (i < text.Length && text[i] == '"')
                {
                    current.Append('\\', count / 2);
                    if (count % 2 == 1)
                    {
                        current.Append('"');
                        continue;
                    }

                    i--;                                                // the quote is handled by the next iteration
                }
                else
                {
                    current.Append('\\', count);
                    i--;
                }

                continue;
            }

            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            current.Append(c);
        }

        if (inArgument)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
