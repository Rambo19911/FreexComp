using System.Text;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// A form document opened through the document service of a test composition (in-memory disk, test metadata), shown in
/// its Design view: the setting of the Object Inspector, Events page and Tool Palette tests.
/// </summary>
internal sealed class DesignerFixture
{
    public const string Dir = @"C:\p";
    public const string UnitPath = Dir + @"\MainForm.pas";
    public const string FormPath = Dir + @"\MainForm.form";

    /// <summary>The standard unit of these tests: five components, two handlers, a shared handler and a private field.</summary>
    public static readonly string Unit = string.Join("\r\n",
        "unit MainForm;",
        "",
        "interface",
        "",
        "uses",
        "  Classes, Controls, Forms, StdCtrls, ExtCtrls;",
        "",
        "type",
        "  TMainForm = class(TForm)",
        "    NameEdit: TEdit;",
        "    GreetButton: TButton;",
        "    OkButton: TButton;",
        "    InfoLabel: TLabel;",
        "    Panel1: TPanel;",
        "    procedure GreetButtonClick(Sender: TObject);",
        "    procedure SharedClick(Sender: TObject);",
        "    procedure NameEditKeyPress(Sender: TObject; var Key: Char);",
        "  private",
        "    FCount: Integer;",
        "  public",
        "  end;",
        "",
        "var",
        "  MainForm: TMainForm;",
        "",
        "implementation",
        "",
        "{$FORM MainForm.form}",
        "",
        "procedure TMainForm.GreetButtonClick(Sender: TObject);",
        "begin",
        "",
        "end;",
        "",
        "procedure TMainForm.SharedClick(Sender: TObject);",
        "begin",
        "",
        "end;",
        "",
        "procedure TMainForm.NameEditKeyPress(Sender: TObject; var Key: Char);",
        "begin",
        "",
        "end;",
        "",
        "end.",
        "");

    /// <summary>The form of <see cref="Unit"/>.</summary>
    public static readonly string Form = string.Join("\n",
        "object MainForm: TMainForm",
        "  Left = 200",
        "  Top = 120",
        "  Caption = 'MainForm'",
        "  ClientWidth = 400",
        "  ClientHeight = 300",
        "  PixelsPerInch = 96",
        "  object NameEdit: TEdit",
        "    Left = 16",
        "    Top = 16",
        "    Width = 200",
        "    Height = 23",
        "    TabOrder = 0",
        "    Text = 'NameEdit'",
        "  end",
        "  object GreetButton: TButton",
        "    Left = 232",
        "    Top = 15",
        "    Width = 75",
        "    Height = 25",
        "    Caption = '&Greet'",
        "    TabOrder = 1",
        "    OnClick = GreetButtonClick",
        "  end",
        "  object OkButton: TButton",
        "    Left = 232",
        "    Top = 48",
        "    Width = 75",
        "    Height = 25",
        "    Caption = 'OkButton'",
        "    TabOrder = 2",
        "  end",
        "  object InfoLabel: TLabel",
        "    Left = 16",
        "    Top = 56",
        "    Width = 34",
        "    Height = 15",
        "    Caption = 'Info'",
        "  end",
        "  object Panel1: TPanel",
        "    Left = 16",
        "    Top = 100",
        "    Width = 185",
        "    Height = 41",
        "    TabOrder = 3",
        "  end",
        "end",
        "");

    public DesignerFixture(Action<TestCompositionOptions>? configure = null)
    {
        Services = TestComposition.Build(o =>
        {
            o.FileSystem = Memory;
            configure?.Invoke(o);
        });
        ((DocumentService)Services.Documents).DesignCatalog = CodeSyncTests.Catalog;
    }

    public InMemoryFileSystem Memory { get; } = new();

    public IdeServices Services { get; }

    public void File(string path, string text)
    {
        Memory.CreateDirectory(Path.GetDirectoryName(path)!);
        Memory.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    public string Disk(string path) => Encoding.UTF8.GetString(Memory.ReadAllBytes(path));

    /// <summary>Opens <paramref name="unit"/> + <paramref name="form"/> (default: <see cref="Unit"/>, <see cref="Form"/>) in the Design view.</summary>
    public async Task<FormDocumentViewModel> OpenAsync(string? unit = null, string? form = null, bool design = true)
    {
        File(UnitPath, unit ?? Unit);
        File(FormPath, form ?? Form);
        var result = await Services.Documents.OpenAsync(UnitPath);
        Assert.True(result.Success, result.MessageId);
        var document = Assert.IsType<FormDocumentViewModel>(result.Value);
        if (design)
        {
            Assert.True(document.ShowView(DocumentView.Design));
        }

        return document;
    }

    /// <summary>Selects components of the active designer by name (the last becomes primary).</summary>
    public static void Select(FormDesignerViewModel designer, params string[] names) => designer.Surface!.Selection.Set(names);

    public static FormObject Object(FormDesignerViewModel designer, string name) => designer.Model!.FindByName(name)!;

    public static string? Stored(FormDesignerViewModel designer, string name, string path) =>
        Object(designer, name).FindProperty(path)?.Value switch
        {
            null => null,
            StringValue s => s.Value,
            IntegerValue i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IdentValue id => id.Name,
            SetValue set => "[" + string.Join(", ", set.Elements) + "]",
            var other => other.ToString(),
        };
}
