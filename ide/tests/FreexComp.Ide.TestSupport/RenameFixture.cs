namespace FreexComp.Ide.TestSupport;

/// <summary>
/// IDE-PM-138: program <c>App</c> using <c>Unit1</c> in the program file, in <c>Unit2</c>'s interface <c>uses</c> and in
/// <c>Unit3</c>'s implementation <c>uses</c>, plus one qualified call <c>Unit1.Hello</c>. <see cref="Renamed"/> gives the
/// texts after <c>Unit1</c> was renamed to <c>Customers</c>: four references in three files besides the heading.
/// </summary>
public static class RenameFixture
{
    public const string ProjectFile = """
        { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
          "files": [ { "path": "App.pas" }, { "path": "Unit1.pas" }, { "path": "Unit2.pas" }, { "path": "Unit3.pas" } ] }
        """;

    public const string App = "program App;\n\nuses Unit1, Unit2, Unit3;\n\nbegin\n  Hello;\n  Unit1.Hello;\n  Two;\n  Three;\nend.\n";

    public const string Unit1 = "unit Unit1;\n\ninterface\n\nprocedure Hello;\n\nimplementation\n\nprocedure Hello;\nbegin\n  WriteLn('hello from the unit');\nend;\n\nend.\n";

    public const string Unit2 = "unit Unit2;\n\ninterface\n\nuses Unit1;\n\nprocedure Two;\n\nimplementation\n\nprocedure Two;\nbegin\n  Hello;\nend;\n\nend.\n";

    public const string Unit3 = "unit Unit3;\n\ninterface\n\nprocedure Three;\n\nimplementation\n\nuses Unit1;\n\nprocedure Three;\nbegin\n  Hello;\nend;\n\nend.\n";

    /// <summary>The text of <paramref name="source"/> after the rename of <c>Unit1</c> to <c>Customers</c>.</summary>
    public static string Renamed(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Replace("Unit1", "Customers", StringComparison.Ordinal);
    }
}
