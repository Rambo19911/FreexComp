using static FreexComp.Designer.Tests.Support.InheritanceFixtures;

namespace FreexComp.Designer.Tests.Model;

/// <summary>Validation of descendant documents (form-inheritance-designer.md DSG-FD-136; D-129).</summary>
public sealed class InheritedFormValidationTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-136")]
    public void OverrideLinesValidated_ReferencesToInheritedResolve()
    {
        var ancestors = new Designer.Model.FrameCatalog();
        AddLevel(ancestors, BaseDialog, "BaseDialogUnit", "TForm");

        var document = ParseWith("""
            inherited MidDialog: TMidDialog
              ActiveControl = OkButton
              Nope = 1
              inherited TitleLabel: TLabel
                FocusControl = Ghost
              end
              inherited OkButton: TButton
                Width = 'wide'
                TabOrder = -5
              end
              object NameLabel: TLabel
                Left = 8
                Top = 40
                Width = 60
                Height = 15
                FocusControl = CancelButton
              end
            end

            """, "TBaseDialog", null, ancestors, "mid.form");

        // Override lines of the root and of inherited objects are checked against their class; references to
        // inherited components resolve in the file scope (ActiveControl, FocusControl = CancelButton).
        Assert.Equal(
        [
            "mid.form(3,3): warning F0102: Property Nope does not exist on TMidDialog",
            "mid.form(5,20): warning F0105: Component Ghost not found",
            "mid.form(8,13): warning F0103: Invalid value for property Width: value kind does not fit type Integer",
            "mid.form(9,16): warning F0103: Invalid value for property TabOrder: value out of range -1..32767",
        ], document.Diagnostics.Select(d => d.ToString()));
        Assert.Equal(["MidDialog", "TitleLabel", "OkButton", "OkButton"], document.Diagnostics.Select(d => d.ObjectName));
    }
}
