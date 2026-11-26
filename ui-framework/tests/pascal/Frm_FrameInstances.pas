// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-430, UIF-1390, UIF-1391, UIF-1392, UIF-1393, UIF-1394, UIF-1396
// needs: desktop
// phase: 8
program Frm_FrameInstances;
{ U-154, application-forms.md "Frames" (UIF-1390...1394; card F05-006 acceptance 3, run-time half): TFrame is a
  registered borderless windowed container (no caption, border or system menu; WS_CHILD, WS_CLIPCHILDREN,
  WS_CLIPSIBLINGS; WS_TABSTOP only with TabStop) with the defaults of UIF-1391; TFrame.Create without a resource
  raises nothing, a TFrame descendant without one raises EResNotFound (as a form, UIF-430). A form with two inline
  instances of a frame unit: every child of both instances exists, owned by and parented to its instance, with the
  frame's values; the instance that overrides the label's Caption shows its own text, the other keeps the
  frame's; each instance keeps the name and the Left of its inline object. Frames created in code with the form as
  owner have no name, two of them coexist and own their children. The form's ActiveControl and the tab cycle
  enter the frames' children (the frames themselves are no tab stops). The reader finds the class of an inline
  object through RegisterClass/GetClass (the frame unit registers its class, UIF-1396) or, since RTL-1105, through a
  published field of the root's class chain; this test's host TForm has no such field, so it still registers its
  classes. A frame class found by neither is F0101 at the type name. The form is shown for the handles. }
uses
  Windows, Messages, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, RTLConsts, TestKit, Frm_AddressFrame;

type
  TFrameForm = class(TForm)
  published
    BillingAddress: TAddressFrame;
    ShippingAddress: TAddressFrame;
    OkButton: TButton;
  end;

  TNoResourceFrame = class(TFrame)
  end;

  TUnregisteredFrame = class(TFrame)
  end;

{$FORM Frm_FrameInstances.form}

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

function Path(C: TComponent): string;
begin
  if C = nil then
    Result := 'nil'
  else if (C.Owner <> nil) and (C.Owner is TFrame) then
    Result := NameOf(C.Owner) + '.' + NameOf(C)
  else
    Result := NameOf(C);
end;

function YesNo(B: Boolean): string;
begin
  if B then
    Result := 'yes'
  else
    Result := 'no';
end;

procedure DumpInstance(F: TAddressFrame);
var
  I: Integer;
  C: TComponent;
  Line: string;
begin
  WriteLn(NameOf(F), ': ', F.ClassName, ' owner=', NameOf(F.Owner), ' parent=', NameOf(F.Parent), ' bounds=',
    Bounds(F), ' inline=', YesNo(csInline in F.ComponentState), ' loading=', YesNo(csLoading in F.ComponentState));
  for I := 0 to F.ComponentCount - 1 do
  begin
    C := F.Components[I];
    Line := '  ' + NameOf(C) + ': ' + C.ClassName + ' owner=' + NameOf(C.Owner);
    if C is TControl then
      Line := Line + ' parent=' + NameOf(TControl(C).Parent) + ' bounds=' + Bounds(TControl(C));
    if C is TLabel then
      Line := Line + ' caption=' + QuoteStr(TLabel(C).Caption);
    if C is TEdit then
      Line := Line + ' text=' + QuoteStr(TEdit(C).Text) + ' taborder=' + IntToStr(TEdit(C).TabOrder);
    WriteLn(Line);
  end;
end;

function HasStyle(H: HWND; Style: Cardinal): Boolean;
begin
  Result := (GetWindowLongPtrW(H, GWL_STYLE) and Style) = Style;
end;

var
  F: TFrameForm;
  Host: TForm;
  Fr: TFrame;
  A, B: TAddressFrame;
  C: TWinControl;
  Cycle, Msg: string;
  I: Integer;
begin
  Section('TFrame (UIF-1390, UIF-1391)');
  Check(GetClass('TFrame') = TFrame, 'TFrame is registered');
  Msg := '';
  Fr := nil;
  try
    Fr := TFrame.Create(nil);
  except
    on E: Exception do
      Msg := E.ClassName + ': ' + E.Message;
  end;
  CheckEqS('', Msg, 'TFrame.Create without a resource raises nothing');
  CheckEqS('0,0,320,240', Bounds(Fr), 'default size');
  CheckEqB(False, Fr.TabStop, 'TabStop default');
  CheckEqI(clBtnFace, Fr.Color, 'Color default');
  CheckEqB(True, Fr.ParentColor, 'ParentColor default');
  CheckEqB(True, Fr.ParentFont, 'ParentFont default');
  CheckEqB(True, Fr.ParentShowHint, 'ParentShowHint default');
  CheckEqB(True, Fr.AutoScroll, 'AutoScroll default');
  CheckEqS('', Fr.Name, 'no name');
  Host := TForm.CreateNew(nil);
  Fr.Parent := Host;
  Fr.HandleNeeded;
  Check(HasStyle(Fr.Handle, WS_CHILD or WS_CLIPCHILDREN or WS_CLIPSIBLINGS), 'WS_CHILD, WS_CLIPCHILDREN, WS_CLIPSIBLINGS');
  CheckEqB(False, HasStyle(Fr.Handle, WS_CAPTION), 'no caption');
  CheckEqB(False, HasStyle(Fr.Handle, WS_BORDER), 'no border');
  CheckEqB(False, HasStyle(Fr.Handle, WS_SYSMENU), 'no system menu');
  CheckEqB(False, HasStyle(Fr.Handle, WS_THICKFRAME), 'no sizing frame');
  CheckEqB(False, HasStyle(Fr.Handle, WS_TABSTOP), 'no WS_TABSTOP');
  CheckEqI(0, GetWindowLongPtrW(Fr.Handle, GWL_EXSTYLE) and WS_EX_CLIENTEDGE, 'no client edge');
  Fr := TFrame.Create(Host);
  Fr.TabStop := True;
  Fr.Parent := Host;
  Fr.HandleNeeded;
  CheckEqB(True, HasStyle(Fr.Handle, WS_TABSTOP), 'WS_TABSTOP with TabStop');
  Host.Free;

  Section('a TFrame descendant without a resource (UIF-1392)');
  Msg := '';
  try
    TNoResourceFrame.Create(nil).Free;
  except
    on E: Exception do
      Msg := E.ClassName + ': ' + E.Message;
  end;
  CheckEqS('EResNotFound: ' + Format(SResNotFound, ['TNoResourceFrame']), Msg, 'EResNotFound');

  Section('the class of an instance is found by its registered name (UIF-1396)');
  Check(GetClass('TAddressFrame') = TAddressFrame, 'the frame unit registers its class');
  Host := TForm.CreateNew(nil);
  Msg := '';
  try
    ReadComponentFromText('object Host: TForm'#10'  inline Lost: TUnregisteredFrame'#10'  end'#10'end'#10, Host,
      'Host.form');
  except
    on E: Exception do
      Msg := E.ClassName + ': ' + E.Message;
  end;
  CheckEqS('EClassNotFound: Host.form(2,16): error F0101: Class TUnregisteredFrame not found', Msg,
    'an unregistered frame class');
  Host.Free;

  Section('two instances on a form (UIF-1392, UIF-1394)');
  Application.CreateForm(TFrameForm, F);
  DumpInstance(F.BillingAddress);
  DumpInstance(F.ShippingAddress);
  CheckEqS('Billing address', F.BillingAddress.TitleLabel.Caption, 'the overridden Caption');
  CheckEqS('Address', F.ShippingAddress.TitleLabel.Caption, 'the other instance keeps the frame''s Caption');
  CheckEqS('Street', F.BillingAddress.StreetEdit.Text, 'a value of the frame in the overriding instance');
  CheckEqI(16, F.BillingAddress.Left, 'the Left of the first instance');
  CheckEqI(344, F.ShippingAddress.Left, 'the Left of the second instance');
  Check(F.FindComponent('TitleLabel') = nil, 'the frame''s children are not owned by the form');
  CheckEqS('BillingAddress', NameOf(F.FindComponent('BillingAddress')), 'the instance is owned by the form');

  Section('frames created in code (UIF-1393)');
  A := TAddressFrame.Create(F);
  B := TAddressFrame.Create(F);
  CheckEqS('', A.Name, 'the first has no name');
  CheckEqS('', B.Name, 'the second has no name');
  Check(A.Owner = F, 'owned by the form');
  CheckEqI(3, A.ComponentCount, 'it owns its three children');
  CheckEqS('Address', B.TitleLabel.Caption, 'with the frame''s values');
  A.Free;
  B.Free;

  Section('tab order across the frames (UIF-1394)');
  F.Show;
  F.ActiveControl := F.BillingAddress.StreetEdit;
  CheckEqS('BillingAddress.StreetEdit', Path(F.ActiveControl), 'ActiveControl inside a frame');
  Cycle := '';
  C := F.BillingAddress.StreetEdit;
  for I := 1 to 5 do
  begin
    C := F.FindNextControl(C, True, True, False);
    Cycle := Cycle + Path(C) + ';';
  end;
  CheckEqS('BillingAddress.CityEdit;ShippingAddress.StreetEdit;ShippingAddress.CityEdit;OkButton;' +
    'BillingAddress.StreetEdit;', Cycle, 'forward cycle');
  C := F.FindNextControl(F.BillingAddress.StreetEdit, False, True, False);
  CheckEqS('OkButton', Path(C), 'backward from the first');
  F.Free;
  Done;
end.
