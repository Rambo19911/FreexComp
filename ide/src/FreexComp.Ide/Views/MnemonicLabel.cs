using System.Text;

namespace FreexComp.Ide.Views;

/// <summary>
/// A label or button resource in the notation of dialogs.md (<c>&amp;</c> before the mnemonic letter, a doubled
/// <c>&amp;&amp;</c> for the character itself) for a control that draws access text (I-286): the mnemonic letter is the
/// access key and the marker is never drawn.
/// </summary>
internal static class MnemonicLabel
{
    /// <summary>
    /// The content of a label or button: <c>&amp;New</c> → <c>_New</c>, <c>a &amp;&amp; b</c> → <c>a &amp; b</c>, and an
    /// underscore of the text doubled so that it is drawn.
    /// </summary>
    public static string ToAccessText(string label) => Convert(label, "_", "__");

    /// <summary>The text without the marker — the screen-reader name of the control.</summary>
    public static string ToPlainText(string label) => Convert(label, "", "_");

    private static string Convert(string label, string marker, string underscore)
    {
        ArgumentNullException.ThrowIfNull(label);
        var text = new StringBuilder(label.Length + 2);
        for (var i = 0; i < label.Length; i++)
        {
            switch (label[i])
            {
                case '&' when i + 1 < label.Length && label[i + 1] == '&':
                    text.Append('&');
                    i++;
                    break;
                case '&':
                    text.Append(marker);
                    break;
                case '_':
                    text.Append(underscore);
                    break;
                default:
                    text.Append(label[i]);
                    break;
            }
        }

        return text.ToString();
    }
}
