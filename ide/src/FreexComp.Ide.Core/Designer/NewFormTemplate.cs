namespace FreexComp.Ide.Core.Designer;

/// <summary>
/// The new form unit (code-sync.md §11): the unit and form texts of the <c>gui-pascal</c> template
/// (<c>Templates/gui-pascal/Form.pas.txt</c>, <c>Form.form.txt</c>), used by <c>file.newForm</c> /
/// <c>project.addNewForm</c> and by the template itself, and the default names (DSG-CS-110).
/// </summary>
public static class NewFormTemplate
{
    /// <summary>DSG-CS-110: the name of the first form of a project (unit and form).</summary>
    public const string FirstFormName = "MainForm";

    /// <summary>The unit text with LF line ends (the caller applies the line-ending setting).</summary>
    public static string UnitText(string unitName, string formName) => Expand(Read("Form.pas.txt"), unitName, formName);

    /// <summary>The form text (UTF-8, LF, like every form file).</summary>
    public static string FormText(string formName) => Expand(Read("Form.form.txt"), formName, formName);

    /// <summary>
    /// DSG-CS-110: <see cref="FirstFormName"/> when the project has no form yet and the name is free, else <c>Form&lt;N&gt;</c>
    /// with the smallest free N ≥ 2. <paramref name="isTaken"/> tells whether a unit name is used (a project unit or a
    /// file in the target folder).
    /// </summary>
    public static string NextName(bool projectHasForm, Func<string, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(isTaken);
        if (!projectHasForm && !isTaken(FirstFormName))
        {
            return FirstFormName;
        }

        var n = 2;
        while (isTaken($"Form{n}"))
        {
            n++;
        }

        return $"Form{n}";
    }

    private static string Expand(string text, string unitName, string formName) =>
        text.Replace("$(UnitName)", unitName, StringComparison.Ordinal)
            .Replace("$(FormName)", formName, StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal);

    private static string Read(string file)
    {
        var assembly = typeof(NewFormTemplate).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.Replace('\\', '/') == "Templates/gui-pascal/" + file);
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
