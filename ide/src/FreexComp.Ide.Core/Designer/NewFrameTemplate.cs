namespace FreexComp.Ide.Core.Designer;

/// <summary>
/// The new frame unit (DSG-CS-114, 115): the unit and form texts of <c>Templates/gui-pascal/Frame.pas.txt</c> and
/// <c>Frame.form.txt</c>, used by <c>file.newFrame</c> / <c>project.addNewFrame</c>, and the default names. The unit is
/// the new form unit with <c>class(TFrame)</c>, without the <c>var</c> section, and registers its class in the
/// <c>initialization</c> section (Q-P8A-15: the reader finds the class of an <c>inline</c> object by its name).
/// </summary>
public static class NewFrameTemplate
{
    /// <summary>The unit text with LF line ends (the caller applies the line-ending setting).</summary>
    public static string UnitText(string unitName, string frameName) => Expand(NewFormTemplate.Read("Frame.pas.txt"), unitName, frameName);

    /// <summary>The form text (UTF-8, LF): the <c>defaultSize</c> of <c>TFrame</c> at 0, 0 (UIF-1395).</summary>
    public static string FormText(string frameName) => Expand(NewFormTemplate.Read("Frame.form.txt"), frameName, frameName);

    /// <summary>
    /// DSG-CS-115: <c>Frame&lt;N&gt;</c> with the smallest free N ≥ 1; <paramref name="isTaken"/> tells whether a unit name is
    /// used (a project unit or a file in the target folder, as DSG-CS-110).
    /// </summary>
    public static string NextName(Func<string, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(isTaken);
        var n = 1;
        while (isTaken($"Frame{n}"))
        {
            n++;
        }

        return $"Frame{n}";
    }

    private static string Expand(string text, string unitName, string frameName) =>
        text.Replace("$(UnitName)", unitName, StringComparison.Ordinal)
            .Replace("$(FrameName)", frameName, StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal);
}
