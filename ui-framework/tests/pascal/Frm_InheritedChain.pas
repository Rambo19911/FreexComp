// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1450, UIF-1451, UIF-1452, UIF-1453, UIF-1454
// phase: 8
program Frm_InheritedChain;
{ U-160, card F05-007 acceptance 1 (run-time half), ui-framework/phase8/form-inheritance.md section 4: a chain of
  three form classes in three units, TBaseDialog (Frm_InheritedBase) -> TRenameDialog (Frm_InheritedMid) ->
  TChainDialog (this program), each with its own .form, is constructed by TForm's constructor with no new framework
  API (UIF-1450). Level 3 overrides level-2 overrides again (the root's Caption, OkButton's Caption), moves OkButton,
  overrides a level-2 component, adds a button to the inherited ButtonPanel and an edit to the root whose TabOrder
  ties with level 1's page control. Printed for each class created alone: every control's bounds, captions and
  parent in Controls[] order (inherited children first, base-most level first, then the new ones, UIF-1452); the
  tab order of ButtonPanel and of the root (the TabOrder values of all levels sorted, a tie in creation order,
  UIF-1451); one Loaded and one OnCreate per construction; the page control of level 1, whose active page level 2
  overrides, ends with one visible page and one OnShow (form-inheritance.md section 9 risk 5); the Name is the
  header name of the most derived file, so all three forms coexist under Application (UIF-1453); OkButton's
  OnClick, named in level 1's file, runs the most derived hiding handler and each "inherited;" reaches the
  ancestors' (UIF-1454). Tab sheet bounds are not printed (the page rectangle depends on the font, UIF-219);
  Frm_InheritedDump has them. Needs no desktop: no form is shown. }
uses
  SysUtils, Classes, Controls, Forms, StdCtrls, ExtCtrls, ComCtrls, TestKit, Frm_InheritedBase, Frm_InheritedMid;

type
  TChainDialog = class(TRenameDialog)
  published
    ApplyButton: TButton;
    NoteEdit: TEdit;
    procedure OkButtonClick(Sender: TObject);
  end;

{$FORM Frm_InheritedChain.form}

procedure TChainDialog.OkButtonClick(Sender: TObject);
begin
  Log := Log + 'chain>';
  inherited;
  Log := Log + '<chain;';
end;

function NameOf(C: TComponent): string;
begin
  if C = nil then
    Result := 'nil'
  else if C.Name = '' then
    Result := '<' + C.ClassName + '>'
  else
    Result := C.Name;
end;

function YesNo(B: Boolean): string;
begin
  if B then
    Result := 'yes'
  else
    Result := 'no';
end;

function Bounds(C: TControl): string;
begin
  Result := IntToStr(C.Left) + ',' + IntToStr(C.Top) + ',' + IntToStr(C.Width) + ',' + IntToStr(C.Height);
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
    Line := Indent + NameOf(C) + ': ' + C.ClassName + ' owner=' + NameOf(C.Owner) + ' parent=' + NameOf(C.Parent);
    if not (C is TTabSheet) then
      Line := Line + ' bounds=' + Bounds(C);
    if C is TButton then
      Line := Line + ' caption=' + QuoteStr(TButton(C).Caption);
    if C is TTabSheet then
      Line := Line + ' caption=' + QuoteStr(TTabSheet(C).Caption) + ' visible=' + YesNo(C.Visible);
    if C is TEdit then
      Line := Line + ' text=' + QuoteStr(TEdit(C).Text);
    WriteLn(Line);
    if C is TWinControl then
      DumpControls(TWinControl(C), Indent + '  ');
  end;
end;

{ the tab order of P's own children (UIF-102) }
function TabOrderOf(P: TWinControl): string;
var
  List: TList;
  I: Integer;
begin
  Result := '';
  List := TList.Create;
  try
    P.GetTabOrderList(List);
    for I := 0 to List.Count - 1 do
      if TWinControl(List[I]).Parent = P then
        Result := Result + TWinControl(List[I]).Name + ';';
  finally
    List.Free;
  end;
end;

function StillLoading(C: TComponent): Boolean;
var
  I: Integer;
begin
  Result := (csLoading in C.ComponentState) or (csReading in C.ComponentState);
  for I := 0 to C.ComponentCount - 1 do
    if StillLoading(C.Components[I]) then
      Result := True;
end;

procedure Dump(F: TBaseDialog);
begin
  WriteLn(NameOf(F), ': ', F.ClassName, ' owner=', NameOf(F.Owner), ' caption=', QuoteStr(F.Caption), ' at=',
    F.Left, ',', F.Top, ' client=', F.ClientWidth, 'x', F.ClientHeight, ' components=', F.ComponentCount);
  DumpControls(F, '  ');
  WriteLn('  tab order: root ', TabOrderOf(F), ' ButtonPanel ', TabOrderOf(F.ButtonPanel));
end;

{ creates one form of AClass, counting Loaded, OnCreate and the page events of the construction }
function Make(AClass: TComponentClass; var Reference): TBaseDialog;
begin
  LoadedCount := 0;
  CreateCount := 0;
  Log := '';
  Application.CreateForm(AClass, Reference);
  Result := TBaseDialog(Reference);
  WriteLn('construction of ', AClass.ClassName, ': Loaded ', LoadedCount, ' OnCreate ', CreateCount, ' pages ',
    QuoteStr(Log), ' loading flags left ', YesNo(StillLoading(Result)));
end;

procedure Click(F: TBaseDialog);
begin
  Log := '';
  F.OkButton.Click;
  WriteLn('  OkButton.Click: ', Log);
end;

var
  BaseDialog: TBaseDialog;
  RenameDialog: TRenameDialog;
  ChainDialog: TChainDialog;
  F: TBaseDialog;
begin
  Section('each level created alone (UIF-1450...1454)');
  F := Make(TBaseDialog, BaseDialog);
  Dump(F);
  Click(F);
  F.Free;
  F := Make(TRenameDialog, RenameDialog);
  Dump(F);
  Click(F);
  F.Free;
  F := Make(TChainDialog, ChainDialog);
  Dump(F);
  Click(F);
  CheckEqI(1, LoadedCount, 'one Loaded for the three levels');
  CheckEqS('Rename item', F.Caption, 'the root''s override of an override');
  CheckEqS('108,8,75,25', Bounds(F.OkButton), 'level 3 moved OkButton');
  CheckEqS('Rename it', F.OkButton.Caption, 'OkButton''s Caption of level 3 wins over level 2');
  CheckEqS('AdvancedPage', F.Pages.ActivePage.Name, 'the active page of level 2');
  CheckEqS('ButtonPanel;NameEdit;Pages;NoteEdit;', TabOrderOf(F), 'the root''s tab order: levels merged, the tie in ' +
    'creation order');
  F.Free;

  Section('the three levels coexist under Application (UIF-1453)');
  Make(TBaseDialog, BaseDialog);
  Make(TRenameDialog, RenameDialog);
  Make(TChainDialog, ChainDialog);
  CheckEqS('BaseDialog', BaseDialog.Name, 'level 1');
  CheckEqS('RenameDialog', RenameDialog.Name, 'level 2 while BaseDialog exists');
  CheckEqS('ChainDialog', ChainDialog.Name, 'level 3 while both exist');
  Check(Application.FindComponent('ChainDialog') = ChainDialog, 'owned by Application under its own name');
  CheckEqI(3, Screen.FormCount, 'three forms');
  ChainDialog.Free;
  RenameDialog.Free;
  BaseDialog.Free;
  Done;
end.
