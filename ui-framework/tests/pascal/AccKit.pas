// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
unit AccKit;
{ U-155: shared helpers of the Acc_* programs (ui-automation.md section 9): the properties of a provider read
  through IRawElementProviderSimple.GetPropertyValue as text, control-type ids as their reference names, results
  as words. }

interface

uses
  Windows, ActiveX, UIAutomation, SysUtils, Controls, Accessibility;

{ the value of a property: the string of a VT_BSTR, 'True' / 'False' of a VT_BOOL, the number of a VT_I4, '<empty>'
  for VT_EMPTY, '<error XXXXXXXX>' when the call fails }
function Prop(const P: IRawElementProviderSimple; Id: Integer): string;
function PropOf(C: TControl; Id: Integer): string;
{ the control type of a control as its reference name ('Button', 'Pane', ...) }
function ControlTypeOf(C: TControl): string;
function TypeWord(Id: Integer): string;
function ResultWord(R: HResult): string;

implementation

function ResultWord(R: HResult): string;
begin
  if R = S_OK then
    Result := 'S_OK'
  else if R = UIA_E_ELEMENTNOTAVAILABLE then
    Result := 'UIA_E_ELEMENTNOTAVAILABLE'
  else if R = UIA_E_INVALIDOPERATION then
    Result := 'UIA_E_INVALIDOPERATION'
  else if R = UIA_E_ELEMENTNOTENABLED then
    Result := 'UIA_E_ELEMENTNOTENABLED'
  else
    Result := 'other ' + IntToHex(Cardinal(R), 8);
end;

function Prop(const P: IRawElementProviderSimple; Id: Integer): string;
var
  V: TVariantArg;
  R: HResult;
begin
  if P = nil then
  begin
    Result := '<no provider>';
    Exit;
  end;
  FillChar(V, SizeOf(V), 0);
  R := P.GetPropertyValue(Id, V);
  if R <> S_OK then
    Result := '<' + ResultWord(R) + '>'
  else if V.vt = VT_EMPTY then
    Result := '<empty>'
  else if V.vt = VT_BSTR then
    Result := string(PWideChar(V.bstrVal))
  else if V.vt = VT_BOOL then
  begin
    if V.boolVal <> 0 then
      Result := 'True'
    else
      Result := 'False';
  end
  else if V.vt = VT_I4 then
    Result := IntToStr(V.lVal)
  else
    Result := '<vt ' + IntToStr(V.vt) + '>';
  VariantClear(V);
end;

function PropOf(C: TControl; Id: Integer): string;
begin
  Result := Prop(GetAutomationProvider(C), Id);
end;

function TypeWord(Id: Integer): string;
begin
  case Id of
    UIA_ButtonControlTypeId: Result := 'Button';
    UIA_CheckBoxControlTypeId: Result := 'CheckBox';
    UIA_ComboBoxControlTypeId: Result := 'ComboBox';
    UIA_EditControlTypeId: Result := 'Edit';
    UIA_ImageControlTypeId: Result := 'Image';
    UIA_ListItemControlTypeId: Result := 'ListItem';
    UIA_ListControlTypeId: Result := 'List';
    UIA_MenuItemControlTypeId: Result := 'MenuItem';
    UIA_ProgressBarControlTypeId: Result := 'ProgressBar';
    UIA_RadioButtonControlTypeId: Result := 'RadioButton';
    UIA_ScrollBarControlTypeId: Result := 'ScrollBar';
    UIA_SliderControlTypeId: Result := 'Slider';
    UIA_StatusBarControlTypeId: Result := 'StatusBar';
    UIA_TabControlTypeId: Result := 'Tab';
    UIA_TabItemControlTypeId: Result := 'TabItem';
    UIA_TextControlTypeId: Result := 'Text';
    UIA_ToolBarControlTypeId: Result := 'ToolBar';
    UIA_TreeControlTypeId: Result := 'Tree';
    UIA_TreeItemControlTypeId: Result := 'TreeItem';
    UIA_CustomControlTypeId: Result := 'Custom';
    UIA_GroupControlTypeId: Result := 'Group';
    UIA_ThumbControlTypeId: Result := 'Thumb';
    UIA_DataGridControlTypeId: Result := 'DataGrid';
    UIA_DataItemControlTypeId: Result := 'DataItem';
    UIA_WindowControlTypeId: Result := 'Window';
    UIA_PaneControlTypeId: Result := 'Pane';
    UIA_HeaderItemControlTypeId: Result := 'HeaderItem';
    UIA_TableControlTypeId: Result := 'Table';
    UIA_SeparatorControlTypeId: Result := 'Separator';
  else
    Result := IntToStr(Id);
  end;
end;

function ControlTypeOf(C: TControl): string;
var
  S: string;
begin
  S := PropOf(C, UIA_ControlTypePropertyId);
  if (S <> '') and (S[1] in ['0'..'9']) then
    Result := TypeWord(StrToInt(S))
  else
    Result := S;
end;

end.
