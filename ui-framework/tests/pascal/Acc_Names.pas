// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1410, UIF-1411, UIF-1412, UIF-1413, UIF-1417, UIF-1418, UIF-1419
// needs: desktop
// phase: 8
program Acc_Names;
{ U-155, accessibility.md sections 4 (UIF-1410...1413, UIF-1417...1419), in process through GetAutomationProvider:
  the accessible properties of TControl (defaults, streamed as ordinary published properties); every source of
  the name (AccessibleName, the own text of each class with the mnemonic rule, the hint of an image and of a tool
  button without caption, the first associated label in creation order, none); HelpText with and without '|';
  AutomationId, ClassName, FrameworkId; the control type of every class of UIF-1417 and an AccessibleRole
  override; LocalizedControlType of a custom role; IsControlElement / IsContentElement of bevel, shape, splitter,
  separator, hidden control; IsEnabled through a disabled parent; a native control has no provider. The form's
  windows are created (HandleNeeded), nothing is shown. }
uses
  Windows, Messages, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ExtCtrls, ComCtrls, Grids, TypInfo,
  UIAutomation, Accessibility, AccKit, TestKit;

type
  { a framework-drawn class without a registration of its own (UIF-1417: Custom) }
  TPlainCustom = class(TCustomControl)
  end;

  TPlainGraphic = class(TGraphicControl)
  end;

var
  F: TForm;

function Add(AClass: TControlClass; AParent: TWinControl; const AName: string): TControl;
begin
  Result := AClass.Create(F);
  if AName <> '' then
    Result.Name := AName;
  Result.Parent := AParent;
  Result.SetBounds(0, 0, 40, 20);
end;

procedure CheckName(C: TControl; const Expected, What: string);
begin
  CheckEqS(Expected, PropOf(C, UIA_NamePropertyId), What);
end;

procedure TestProperties;
var
  L: TLabel;
begin
  Section('accessible properties (UIF-1410)');
  L := TLabel.Create(nil);
  try
    CheckEqS('', L.AccessibleName, 'AccessibleName default');
    CheckEqS('', L.AccessibleDescription, 'AccessibleDescription default');
    Check(L.AccessibleRole = arDefault, 'AccessibleRole default');
    Check(IsPublishedProp(L, 'AccessibleName'), 'AccessibleName is published');
    Check(IsPublishedProp(L, 'AccessibleDescription'), 'AccessibleDescription is published');
    Check(IsPublishedProp(L, 'AccessibleRole'), 'AccessibleRole is published');
    SetStrProp(L, 'AccessibleName', 'streamed');
    CheckEqS('streamed', L.AccessibleName, 'AccessibleName through RTTI');
    SetEnumProp(L, 'AccessibleRole', 'arButton');
    Check(L.AccessibleRole = arButton, 'AccessibleRole through RTTI');
    CheckEqS('arButton', GetEnumProp(L, 'AccessibleRole'), 'AccessibleRole read through RTTI');
  finally
    L.Free;
  end;
end;

procedure TestNames;
var
  P: TPanel;
  G: TGroupBox;
  Pages: TPageControl;
  Sheet: TTabSheet;
  Bar: TToolBar;
  B: TToolButton;
  Img: TImage;
  Box, Box2, Box3: TScrollBox;
  L: TLabel;
  S: TStaticText;
  E: TEdit;
begin
  Section('names (UIF-1411, UIF-1412)');
  F.Caption := 'Names &Form';
  CheckName(F, 'Names Form', 'form: its caption, mnemonic removed');
  L := TLabel(Add(TLabel, F, 'PlainLabel'));
  L.Caption := 'Na&me && more';
  CheckName(L, 'Name & more', 'label: caption; a single & removed, && becomes &');
  L.AccessibleName := 'Given';
  CheckName(L, 'Given', 'AccessibleName wins over the caption');
  P := TPanel(Add(TPanel, F, 'APanel'));
  P.Caption := 'Panel &text';
  CheckName(P, 'Panel text', 'panel: caption');
  G := TGroupBox(Add(TGroupBox, F, 'AGroup'));
  G.Caption := '&Options';
  CheckName(G, 'Options', 'group box: caption');
  Pages := TPageControl(Add(TPageControl, F, 'Pages'));
  Pages.SetBounds(0, 0, 200, 100);
  Sheet := TTabSheet.Create(F);
  Sheet.Name := 'ASheet';
  Sheet.PageControl := Pages;
  Sheet.Caption := '&General';
  CheckName(Sheet, 'General', 'tab sheet: caption');
  Bar := TToolBar(Add(TToolBar, F, 'Bar'));
  B := TToolButton.Create(F);
  B.Name := 'CaptionButton';
  B.Parent := Bar;
  B.Caption := '&Save';
  CheckName(B, 'Save', 'tool button: caption');
  B := TToolButton.Create(F);
  B.Name := 'HintButton';
  B.Parent := Bar;
  B.Caption := '';
  B.Hint := 'Open|Opens a file';
  CheckName(B, 'Open', 'tool button without caption: its hint (the short part)');
  Img := TImage(Add(TImage, F, 'Picture'));
  Img.Hint := 'Company logo';
  CheckName(Img, 'Company logo', 'image: its hint');
  Box := TScrollBox(Add(TScrollBox, F, 'Unlabelled'));
  CheckName(Box, '', 'no source: empty');
  Box2 := TScrollBox(Add(TScrollBox, F, 'Labelled'));
  L := TLabel(Add(TLabel, F, 'FirstLabel'));
  L.Caption := '&Address';
  L.FocusControl := Box2;
  L := TLabel(Add(TLabel, F, 'SecondLabel'));
  L.Caption := 'Second';
  L.FocusControl := Box2;
  CheckName(Box2, 'Address', 'the first associated label in creation order, mnemonic removed');
  Box2.AccessibleName := 'Own';
  CheckName(Box2, 'Own', 'AccessibleName wins over the associated label');
  Box3 := TScrollBox(Add(TScrollBox, F, 'ByStaticText'));
  S := TStaticText(Add(TStaticText, F, 'Static'));
  S.Caption := 'S&tatic name';
  S.FocusControl := Box3;
  CheckName(Box3, 'Static name', 'a static text is an associated label too');
  E := TEdit(Add(TEdit, F, 'NativeEdit'));
  L.FocusControl := E;
  Check(GetAutomationProvider(E) = nil, 'a native control has no provider (UIF-1420)');
  CheckEqS('Second', AccessibleNameOf(E), 'the name of a native control follows the same rules');
end;

procedure TestOtherProperties;
var
  P: TPanel;
  L: TLabel;
  C: TPlainCustom;
begin
  Section('HelpText, AutomationId, ClassName, FrameworkId (UIF-1413)');
  P := TPanel(Add(TPanel, F, 'HelpPanel'));
  P.Hint := 'Short hint|Long hint';
  CheckEqS('Short hint', PropOf(P, UIA_HelpTextPropertyId), 'HelpText: the hint without the part after |');
  P.Hint := 'Whole hint';
  CheckEqS('Whole hint', PropOf(P, UIA_HelpTextPropertyId), 'HelpText: a hint without |');
  P.AccessibleDescription := 'Described';
  CheckEqS('Described', PropOf(P, UIA_HelpTextPropertyId), 'HelpText: AccessibleDescription wins');
  L := TLabel(Add(TLabel, F, 'Quiet'));
  CheckEqS('<empty>', PropOf(L, UIA_HelpTextPropertyId), 'HelpText: none');
  CheckEqS('HelpPanel', PropOf(P, UIA_AutomationIdPropertyId), 'AutomationId = Name');
  C := TPlainCustom(Add(TPlainCustom, F, ''));
  CheckEqS('', PropOf(C, UIA_AutomationIdPropertyId), 'AutomationId of an unnamed control: empty');
  CheckEqS('TPanel', PropOf(P, UIA_ClassNamePropertyId), 'ClassName');
  CheckEqS('TPlainCustom', PropOf(C, UIA_ClassNamePropertyId), 'ClassName of a program class');
  CheckEqS('FreexComp', PropOf(P, UIA_FrameworkIdPropertyId), 'FrameworkId');
  CheckEqS('<empty>', PropOf(P, UIA_LocalizedControlTypePropertyId), 'LocalizedControlType: not supplied');
  C.AccessibleRole := arCustom;
  C.AccessibleDescription := 'gauge';
  CheckEqS('gauge', PropOf(C, UIA_LocalizedControlTypePropertyId), 'LocalizedControlType of arCustom: the description');
  CheckEqS('<empty>', PropOf(P, UIA_IsKeyboardFocusablePropertyId), 'a window: focusability from the host');
  CheckEqS('False', PropOf(L, UIA_IsKeyboardFocusablePropertyId), 'a graphic control is not focusable');
  CheckEqS('False', PropOf(L, UIA_HasKeyboardFocusPropertyId), 'a graphic control has no focus');
end;

procedure TestControlTypes;
var
  Bar: TToolBar;
  Pages: TPageControl;
  Sheet: TTabSheet;
  P: TPanel;
  Frame: TFrame;
  S: TToolButtonStyle;
  B: TToolButton;
begin
  Section('control types (UIF-1417)');
  CheckEqS('Window', ControlTypeOf(F), 'form');
  Frame := TFrame.Create(F);
  Frame.Parent := F;
  CheckEqS('Pane', ControlTypeOf(Frame), 'frame');
  CheckEqS('Pane', ControlTypeOf(Add(TPanel, F, '')), 'panel');
  CheckEqS('Pane', ControlTypeOf(Add(TScrollBox, F, '')), 'scroll box');
  Pages := TPageControl(Add(TPageControl, F, ''));
  Sheet := TTabSheet.Create(F);
  Sheet.PageControl := Pages;
  CheckEqS('Pane', ControlTypeOf(Sheet), 'tab sheet');
  CheckEqS('Group', ControlTypeOf(Add(TGroupBox, F, '')), 'group box');
  Bar := TToolBar(Add(TToolBar, F, ''));
  CheckEqS('ToolBar', ControlTypeOf(Bar), 'tool bar');
  for S := Low(TToolButtonStyle) to High(TToolButtonStyle) do
  begin
    B := TToolButton.Create(F);
    B.Parent := Bar;
    B.Style := S;
    if S in [tbsSeparator, tbsDivider] then
      CheckEqS('Separator', ControlTypeOf(B), 'tool button ' + GetEnumName(TypeInfo(TToolButtonStyle), Ord(S)))
    else
      CheckEqS('Button', ControlTypeOf(B), 'tool button ' + GetEnumName(TypeInfo(TToolButtonStyle), Ord(S)));
  end;
  CheckEqS('Text', ControlTypeOf(Add(TLabel, F, '')), 'label');
  CheckEqS('Image', ControlTypeOf(Add(TImage, F, '')), 'image');
  CheckEqS('Thumb', ControlTypeOf(Add(TSplitter, F, '')), 'splitter');
  CheckEqS('DataGrid', ControlTypeOf(Add(TStringGrid, F, '')), 'grid');
  CheckEqS('Custom', ControlTypeOf(Add(TBevel, F, '')), 'bevel');
  CheckEqS('Custom', ControlTypeOf(Add(TShape, F, '')), 'shape');
  CheckEqS('Custom', ControlTypeOf(Add(TPlainCustom, F, '')), 'another framework window class');
  CheckEqS('Custom', ControlTypeOf(Add(TPlainGraphic, F, '')), 'another graphic class');
  P := TPanel(Add(TPanel, F, ''));
  P.AccessibleRole := arList;
  CheckEqS('List', ControlTypeOf(P), 'AccessibleRole overrides the class default');
  P.AccessibleRole := arTreeItem;
  CheckEqS('TreeItem', ControlTypeOf(P), 'a second override');
end;

procedure CheckKind(C: TControl; const Control, Content, What: string);
begin
  CheckEqS(Control, PropOf(C, UIA_IsControlElementPropertyId), What + ': IsControlElement');
  CheckEqS(Content, PropOf(C, UIA_IsContentElementPropertyId), What + ': IsContentElement');
end;

procedure TestElements;
var
  Bar: TToolBar;
  B: TToolButton;
  Bevel: TBevel;
  Hidden: TLabel;
begin
  Section('control and content elements (UIF-1419)');
  Bevel := TBevel(Add(TBevel, F, ''));
  CheckKind(Bevel, 'False', 'False', 'bevel');
  CheckKind(Add(TShape, F, ''), 'False', 'False', 'shape');
  Bevel.AccessibleRole := arSeparator;
  CheckKind(Bevel, 'True', 'True', 'bevel with a role');
  CheckKind(Add(TSplitter, F, ''), 'True', 'False', 'splitter');
  Bar := TToolBar(Add(TToolBar, F, ''));
  B := TToolButton.Create(F);
  B.Parent := Bar;
  B.Style := tbsSeparator;
  CheckKind(B, 'False', 'False', 'tool bar separator');
  B.Style := tbsDivider;
  CheckKind(B, 'False', 'False', 'tool bar divider');
  B.Style := tbsCheck;
  CheckKind(B, 'True', 'True', 'tool button');
  Hidden := TLabel(Add(TLabel, F, ''));
  Hidden.Visible := False;
  CheckKind(Hidden, 'False', 'False', 'a hidden control');
  CheckKind(Add(TPanel, F, ''), 'True', 'True', 'panel');
  CheckKind(Add(TLabel, F, ''), 'True', 'True', 'label');
end;

procedure TestEnabled;
var
  Outer, Inner: TPanel;
  L: TLabel;
begin
  Section('IsEnabled and IsOffscreen (UIF-1418)');
  Outer := TPanel(Add(TPanel, F, 'Outer'));
  Inner := TPanel(Add(TPanel, Outer, 'Inner'));
  L := TLabel(Add(TLabel, Inner, 'Deep'));
  CheckEqS('True', PropOf(L, UIA_IsEnabledPropertyId), 'enabled chain');
  Outer.Enabled := False;
  CheckEqS('False', PropOf(L, UIA_IsEnabledPropertyId), 'a disabled parent two levels up');
  CheckEqS('False', PropOf(Inner, UIA_IsEnabledPropertyId), 'a disabled parent of a window');
  CheckEqS('False', PropOf(Outer, UIA_IsEnabledPropertyId), 'the disabled control');
  Outer.Enabled := True;
  CheckEqS('True', PropOf(L, UIA_IsEnabledPropertyId), 'enabled again');
  CheckEqS('True', PropOf(L, UIA_IsOffscreenPropertyId), 'the form is not shown: off screen');
end;

begin
  Application.Initialize;
  TestProperties;
  F := TForm.CreateNew(nil);
  try
    F.SetBounds(0, 0, 600, 400);
    F.HandleNeeded;
    TestNames;
    TestOtherProperties;
    TestControlTypes;
    TestElements;
    TestEnabled;
  finally
    F.Free;
  end;
  CheckEqI(0, AutomationProviderCount, 'every provider is released with its form (UIF-1425)');
  Done;
end.
