namespace FreexComp.Ide.Core.Designer;

/// <summary>
/// The new inherited form or frame (form-inheritance-designer.md §7, DSG-CS-124, 125): the texts of
/// <c>Templates/gui-pascal/InheritedForm.pas.txt</c>, <c>InheritedFrame.pas.txt</c> and <c>Inherited.form.txt</c>, used by
/// <c>file.newInheritedForm</c> / <c>project.addNewInheritedForm</c>, and the default name. The form unit is the new form
/// unit with <c>class(T&lt;A&gt;)</c>, the frame unit the new frame unit with it; both list the ancestor's unit in the
/// interface <c>uses</c> (not twice); the form file is the bare <c>inherited</c> root.
/// </summary>
public static class NewInheritedFormTemplate
{
    /// <summary>The units every new form unit uses (DSG-CS-110); the ancestor's unit is not appended when it is one of them.</summary>
    private static readonly string[] StandardUses = ["Classes", "Controls", "Forms"];

    /// <summary>The unit text with LF line ends (the caller applies the line-ending setting).</summary>
    public static string UnitText(string unitName, string ancestorClass, string ancestorUnit, bool isFrame)
    {
        ArgumentNullException.ThrowIfNull(ancestorUnit);
        var text = NewFormTemplate.Read(isFrame ? "InheritedFrame.pas.txt" : "InheritedForm.pas.txt");
        if (StandardUses.Contains(ancestorUnit, StringComparer.OrdinalIgnoreCase))
        {
            text = text.Replace(", $(AncestorUnit)", "", StringComparison.Ordinal);
        }

        return Expand(text, unitName, ancestorClass, ancestorUnit);
    }

    /// <summary>The form text (UTF-8, LF): <c>inherited &lt;N&gt;: T&lt;N&gt;</c> and <c>end</c>.</summary>
    public static string FormText(string formName) => Expand(NewFormTemplate.Read("Inherited.form.txt"), formName, "", "");

    /// <summary>
    /// DSG-CS-125: the ancestor's class name without the leading <c>T</c> plus the smallest free M ≥ 2 (<c>BaseDialog2</c>);
    /// <paramref name="isTaken"/> tells whether a unit name is used (a project unit or a file in the target folder).
    /// </summary>
    public static string NextName(string ancestorClass, Func<string, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(ancestorClass);
        ArgumentNullException.ThrowIfNull(isTaken);
        var stem = ancestorClass.Length > 1 && ancestorClass[0] is 'T' or 't' ? ancestorClass[1..] : ancestorClass;
        var n = 2;
        while (isTaken($"{stem}{n}"))
        {
            n++;
        }

        return $"{stem}{n}";
    }

    private static string Expand(string text, string name, string ancestorClass, string ancestorUnit) =>
        text.Replace("$(UnitName)", name, StringComparison.Ordinal)
            .Replace("$(FormName)", name, StringComparison.Ordinal)
            .Replace("$(AncestorClass)", ancestorClass, StringComparison.Ordinal)
            .Replace("$(AncestorUnit)", ancestorUnit, StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal);
}
