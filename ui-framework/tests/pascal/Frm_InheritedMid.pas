// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
unit Frm_InheritedMid;
{ U-160: level 2 of the inherited-form chain (ui-framework/phase8/form-inheritance.md section 2): TRenameDialog
  inherits the form of TBaseDialog; its .form starts with "inherited" and holds only differences - the root's
  Caption, an override of OkButton, a new button inside the inherited ButtonPanel, the active page of the inherited
  page control and a new edit at the root. Published fields only for the new components; OkButtonClick hides the
  ancestor's handler and calls it with "inherited;". A unit, not a test program (UIF-1200). }

interface

uses
  SysUtils, Classes, Controls, Forms, StdCtrls, Frm_InheritedBase;

type
  TRenameDialog = class(TBaseDialog)
  published
    HelpButton: TButton;
    NameEdit: TEdit;
    procedure OkButtonClick(Sender: TObject);
  end;

implementation

{$FORM Frm_InheritedMid.form}

procedure TRenameDialog.OkButtonClick(Sender: TObject);
begin
  Log := Log + 'mid>';
  inherited;
  Log := Log + '<mid;';
end;

end.
