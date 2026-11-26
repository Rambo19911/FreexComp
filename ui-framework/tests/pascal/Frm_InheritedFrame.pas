// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1450, UIF-1452
// phase: 8
program Frm_InheritedFrame;
{ U-160, ui-framework/phase8/form-inheritance.md section 6 with UIF-1450, UIF-1452: an inherited form whose
  ancestor's file holds an instance of the frame of Frm_AddressFrame with an override of TitleLabel's Caption. The
  descendant's file changes the instance with an "inherited" block (nothing is created): it sets the instance's
  Left and overrides the same label again, so frame default -> ancestor override -> descendant override, the last
  wins. The descendant adds an instance of TAddressFrameEx (Frm_AddressFrameEx), a frame derived from that frame:
  the instance merges both frame levels (the derived frame's Height and label, then the instance's overrides) and,
  with an owner, keeps the name of its inline object (RTL-1134). A TAddressFrameEx created alone takes the header
  name of the most derived frame file; created with an owner it has none. Printed: every control with owner,
  parent, bounds and caption, in Controls[] order (inherited children first). Needs no desktop: no form is shown. }
uses
  SysUtils, Classes, Controls, Forms, StdCtrls, TestKit, Frm_AddressFrame, Frm_AddressFrameEx;

type
  TFrameBaseForm = class(TForm)
  published
    Address: TAddressFrame;
    OkButton: TButton;
  end;

  TFrameChildForm = class(TFrameBaseForm)
  published
    Extra: TAddressFrameEx;
  end;

{$FORM Frm_InheritedFrameBase.form}
{$FORM Frm_InheritedFrame.form}

function NameOf(C: TComponent): string;
begin
  if C = nil then
    Result := 'nil'
  else if C.Name = '' then
    Result := '<' + C.ClassName + '>'
  else
    Result := C.Name;
end;

function Bounds(C: TControl): string;
begin
  Result := IntToStr(C.Left) + ',' + IntToStr(C.Top) + ',' + IntToStr(C.Width) + ',' + IntToStr(C.Height);
end;

function YesNo(B: Boolean): string;
begin
  if B then
    Result := 'yes'
  else
    Result := 'no';
end;

procedure DumpControls(P: TWinControl; const Indent: string);
var
  I: Integer;
  C: TControl;
  Line: string;
begin
  for I := 0 to P.ControlCount - 1 do
  begin
    C := P.Controls[I];
    Line := Indent + NameOf(C) + ': ' + C.ClassName + ' owner=' + NameOf(C.Owner) + ' parent=' + NameOf(C.Parent) +
      ' bounds=' + Bounds(C);
    if C is TFrame then
      Line := Line + ' inline=' + YesNo(csInline in C.ComponentState) + ' components=' + IntToStr(C.ComponentCount);
    if C is TLabel then
      Line := Line + ' caption=' + QuoteStr(TLabel(C).Caption);
    if C is TButton then
      Line := Line + ' caption=' + QuoteStr(TButton(C).Caption);
    if C is TEdit then
      Line := Line + ' text=' + QuoteStr(TEdit(C).Text);
    WriteLn(Line);
    if C is TWinControl then
      DumpControls(TWinControl(C), Indent + '  ');
  end;
end;

procedure Dump(F: TForm);
begin
  WriteLn(NameOf(F), ': ', F.ClassName, ' caption=', QuoteStr(F.Caption), ' components=', F.ComponentCount);
  DumpControls(F, '  ');
end;

var
  Base: TFrameBaseForm;
  F: TFrameChildForm;
  X: TAddressFrameEx;
begin
  Section('the ancestor form alone');
  Base := TFrameBaseForm.Create(nil);
  Dump(Base);
  Base.Free;

  Section('the inherited form (form-inheritance.md section 6)');
  F := TFrameChildForm.Create(nil);
  Dump(F);
  CheckEqS('Home address', F.Address.TitleLabel.Caption, 'override of an override: the descendant''s Caption wins');
  CheckEqI(24, F.Address.Left, 'the descendant''s Left of the inherited instance');
  Check(F.FindComponent('TitleLabel') = nil, 'the frame''s children stay owned by their instance');
  CheckEqS('Delivery address', F.Extra.TitleLabel.Caption, 'the derived frame''s instance overrides a base child');
  CheckEqS('Ring twice', F.Extra.NoteLabel.Caption, 'and the derived frame''s own label');
  CheckEqS('Street', F.Extra.StreetEdit.Text, 'a value of the base frame in the derived frame''s instance');
  CheckEqI(150, F.Extra.Height, 'the Height of the derived frame''s file');
  CheckEqS('Extra', F.Extra.Name, 'the instance keeps the name of its inline object');
  F.Free;

  Section('the derived frame created in code (RTL-1134)');
  X := TAddressFrameEx.Create(nil);
  CheckEqS('AddressFrameEx', X.Name, 'alone: the header name of the most derived frame file');
  CheckEqI(4, X.ComponentCount, 'it owns the base frame''s three children and its own label');
  CheckEqS('Address (extended)', X.TitleLabel.Caption, 'the derived frame''s override');
  X.Free;
  Base := TFrameBaseForm.CreateNew(nil);
  X := TAddressFrameEx.Create(Base);
  CheckEqS('', X.Name, 'with an owner: no name');
  CheckEqI(4, X.ComponentCount, 'with an owner: the same four children');
  Base.Free;
  Done;
end.
