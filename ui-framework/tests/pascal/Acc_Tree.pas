// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1420, UIF-1421, UIF-1422, UIF-1423, UIF-1424, UIF-1425, UIF-1426, UIF-1427, UIF-1440
// needs: desktop
// phase: 8
program Acc_Tree;
{ U-155, accessibility.md section 5 (UIF-1420...1427, UIF-1440), in process: the providers of a framework window
  (fragment root over the host provider) and of its graphic children (fragments): options, host, navigation of the
  graphic control elements in Controls[] order (bevels and hidden controls skipped, child windows left to the
  system), fragment roots, bounding rectangles in screen coordinates, hit testing topmost first, runtime ids
  (unique, appended to the host's), focus; one provider per control; a provider whose control was destroyed,
  removed from its parent or whose window was destroyed answers UIA_E_ELEMENTNOTAVAILABLE; WM_GETOBJECT answered
  only for the UI Automation object id; the registry picks the nearest registered ancestor; a property change of a
  control without provider creates none; every provider object is released at the end. The form is shown without
  activation for real screen coordinates. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ExtCtrls, ActiveX,
  UIAutomation, Accessibility, AccKit, TestKit;

type
  TTestLabel = class(TLabel)
  end;

  TTestSubLabel = class(TTestLabel)
  end;

  { registered for TTestLabel in this program (UIF-1421) }
  TTestLabelProvider = class(TGraphicAutomationProvider)
  public
    class function OwnText(Control: TControl): string; override;
    class function DefaultControlType(Control: TControl): Integer; override;
  end;

class function TTestLabelProvider.OwnText(Control: TControl): string;
begin
  Result := 'registered';
end;

class function TTestLabelProvider.DefaultControlType(Control: TControl): Integer;
begin
  Result := UIA_SliderControlTypeId;
end;

var
  F: TForm;
  Host: TPanel;
  First, Second, Last, Hidden: TLabel;
  Bevel: TBevel;
  Edit: TEdit;
  Img: TImage;

function Add(AClass: TControlClass; AParent: TWinControl; const AName: string; L, T, W, H: Integer): TControl;
begin
  Result := AClass.Create(F);
  Result.Name := AName;
  Result.Parent := AParent;
  Result.SetBounds(L, T, W, H);
end;

function Fragment(C: TControl): IRawElementProviderFragment;
begin
  Result := GetAutomationProvider(C) as IRawElementProviderFragment;
end;

{ the AutomationId of a fragment; 'nil' for none }
function IdOf(const Frag: IRawElementProviderFragment): string;
begin
  if Frag = nil then
    Result := 'nil'
  else
    Result := Prop(Frag as IRawElementProviderSimple, UIA_AutomationIdPropertyId);
end;

function Nav(const Frag: IRawElementProviderFragment; Direction: Integer): string;
var
  Next: IRawElementProviderFragment;
begin
  Next := nil;
  CheckEqS('S_OK', ResultWord(Frag.Navigate(Direction, Next)), 'Navigate ' + IntToStr(Direction) + ' returns S_OK');
  Result := IdOf(Next);
end;

function SameObject(const A, B: IUnknown): Boolean;
var
  X, Y: IUnknown;
begin
  X := A as IUnknown;
  Y := B as IUnknown;
  Result := Pointer(X) = Pointer(Y);
end;

procedure Build;
begin
  F := TForm.CreateNew(nil);
  F.Caption := 'Acc_Tree';
  F.SetBounds(40, 40, 500, 300);
  Host := TPanel(Add(TPanel, F, 'Host', 10, 10, 400, 200));
  Host.Caption := '';
  First := TLabel(Add(TLabel, Host, 'First', 10, 10, 60, 20));
  First.AutoSize := False;
  Bevel := TBevel(Add(TBevel, Host, 'Decoration', 80, 10, 20, 20));
  Edit := TEdit(Add(TEdit, Host, 'Window', 110, 10, 60, 24));
  Hidden := TLabel(Add(TLabel, Host, 'Hidden', 180, 10, 40, 20));
  Hidden.Visible := False;
  Second := TLabel(Add(TLabel, Host, 'Second', 10, 50, 100, 30));
  Second.AutoSize := False;
  Img := TImage(Add(TImage, Host, 'Picture', 200, 60, 40, 40));
  Last := TLabel(Add(TLabel, Host, 'Last', 40, 60, 100, 30));   { overlaps Second, in front of it }
  Last.AutoSize := False;
  F.Show;
end;

procedure TestRootAndHost;
var
  Root: IRawElementProviderSimple;
  Frag: IRawElementProviderFragment;
  FragRoot: IRawElementProviderFragmentRoot;
  HostProvider: IRawElementProviderSimple;
  Options: Integer;
  R: TUiaRect;
  Ids: PSafeArray;
begin
  Section('the provider of a framework window (UIF-1423)');
  Root := GetAutomationProvider(Host);
  Check(Root <> nil, 'a provider');
  Check(SameObject(Root, GetAutomationProvider(Host)), 'one provider per control (UIF-1425)');
  CheckEqS('S_OK', ResultWord(Root.get_ProviderOptions(Options)), 'get_ProviderOptions');
  CheckEqI(ProviderOptions_ServerSideProvider, Options, 'server-side');
  HostProvider := nil;
  CheckEqS('S_OK', ResultWord(Root.get_HostRawElementProvider(HostProvider)), 'get_HostRawElementProvider');
  Check(HostProvider <> nil, 'the host provider of the window');
  HostProvider := nil;
  Frag := Root as IRawElementProviderFragment;
  FragRoot := Root as IRawElementProviderFragmentRoot;
  Check(FragRoot <> nil, 'a fragment root');
  FragRoot := nil;
  CheckEqS('S_OK', ResultWord(Frag.get_FragmentRoot(FragRoot)), 'get_FragmentRoot');
  Check(SameObject(FragRoot, Root), 'its fragment root is itself');
  FragRoot := nil;
  CheckEqS('nil', Nav(Frag, NavigateDirection_Parent), 'no parent (the system knows the window''s parent)');
  CheckEqS('nil', Nav(Frag, NavigateDirection_NextSibling), 'no sibling');
  CheckEqS('S_OK', ResultWord(Frag.GetRuntimeId(Ids)), 'GetRuntimeId');
  Check(Ids = nil, 'no runtime id of its own (the host supplies it)');
  FillChar(R, SizeOf(R), $FF);
  CheckEqS('S_OK', ResultWord(Frag.get_BoundingRectangle(R)), 'get_BoundingRectangle');
  Check((R.left = 0) and (R.top = 0) and (R.width = 0) and (R.height = 0), 'empty (the host supplies it)');
end;

procedure TestNavigation;
var
  Root, A: IRawElementProviderFragment;
begin
  Section('navigation of graphic children (UIF-1423, UIF-1424)');
  Root := Fragment(Host);
  CheckEqS('First', Nav(Root, NavigateDirection_FirstChild), 'first child: the first graphic element');
  CheckEqS('Last', Nav(Root, NavigateDirection_LastChild), 'last child: the last graphic element');
  A := Fragment(First);
  CheckEqS('Second', Nav(A, NavigateDirection_NextSibling), 'bevel, window and hidden label skipped');
  CheckEqS('nil', Nav(A, NavigateDirection_PreviousSibling), 'no element before the first');
  CheckEqS('Host', Nav(A, NavigateDirection_Parent), 'parent: the windowed parent');
  CheckEqS('nil', Nav(A, NavigateDirection_FirstChild), 'a graphic control has no children');
  A := Fragment(Second);
  CheckEqS('Picture', Nav(A, NavigateDirection_NextSibling), 'next of Second');
  CheckEqS('First', Nav(A, NavigateDirection_PreviousSibling), 'previous of Second');
  A := Fragment(Last);
  CheckEqS('nil', Nav(A, NavigateDirection_NextSibling), 'no element after the last');
  CheckEqS('Picture', Nav(A, NavigateDirection_PreviousSibling), 'previous of Last');
  Last.BringToFront;
  Second.BringToFront;
  CheckEqS('Second', Nav(Root, NavigateDirection_LastChild), 'Controls[] order after BringToFront');
  Second.SendToBack;
  CheckEqS('Second', Nav(Root, NavigateDirection_FirstChild), 'Controls[] order after SendToBack');
  Second.BringToFront;
  Last.BringToFront;
end;

procedure TestFragments;
var
  A: IRawElementProviderFragment;
  FragRoot: IRawElementProviderFragmentRoot;
  HostProvider: IRawElementProviderSimple;
  R: TUiaRect;
  P: TPoint;
  Roots: PSafeArray;
  Options: Integer;
begin
  Section('graphic controls are fragments (UIF-1424)');
  A := Fragment(First);
  CheckEqS('S_OK', ResultWord((A as IRawElementProviderSimple).get_ProviderOptions(Options)), 'get_ProviderOptions');
  CheckEqI(ProviderOptions_ServerSideProvider, Options, 'server-side');
  PPointer(@HostProvider)^ := nil;
  CheckEqS('S_OK', ResultWord((A as IRawElementProviderSimple).get_HostRawElementProvider(HostProvider)),
    'get_HostRawElementProvider');
  Check(HostProvider = nil, 'no host provider');
  FragRoot := nil;
  CheckEqS('S_OK', ResultWord(A.get_FragmentRoot(FragRoot)), 'get_FragmentRoot');
  Check(SameObject(FragRoot, GetAutomationProvider(Host)), 'the fragment root is the parent''s provider');
  FragRoot := nil;
  CheckEqS('S_OK', ResultWord(A.get_BoundingRectangle(R)), 'get_BoundingRectangle');
  P := Point(First.Left, First.Top);
  Windows.ClientToScreen(Host.Handle, P);
  Check((Round(R.left) = P.X) and (Round(R.top) = P.Y), 'left, top: ClientToScreen of the parent');
  CheckEqI(First.Width, Round(R.width), 'width');
  CheckEqI(First.Height, Round(R.height), 'height');
  CheckEqS('S_OK', ResultWord(A.GetEmbeddedFragmentRoots(Roots)), 'GetEmbeddedFragmentRoots');
  Check(Roots = nil, 'no embedded fragment roots');
  CheckEqS('UIA_E_INVALIDOPERATION', ResultWord(A.SetFocus), 'SetFocus: graphic controls cannot take the focus');
end;

function RuntimeId(C: TControl; out First, Second: Integer): Integer;
var
  Ids: PSafeArray;
  Data: PInteger;
begin
  Fragment(C).GetRuntimeId(Ids);
  Result := 0;
  First := 0;
  Second := 0;
  if Ids = nil then
    Exit;
  Result := PCardinal(NativeUInt(Ids) + 16 + SizeOf(Pointer))^;   { rgsabound[0].cElements }
  Data := PPointer(NativeUInt(Ids) + 16)^;                        { pvData }
  First := Data^;
  Second := PInteger(NativeUInt(Data) + 4)^;
  SafeArrayDestroy(Ids);
end;

procedure TestRuntimeIds;
var
  N1, N2, A1, A2, B1, B2: Integer;
begin
  Section('runtime ids (UIF-1424)');
  N1 := RuntimeId(First, A1, A2);
  N2 := RuntimeId(Second, B1, B2);
  CheckEqI(2, N1, 'two elements');
  CheckEqI(2, N2, 'two elements (second control)');
  CheckEqI(UiaAppendRuntimeId, A1, 'first element: UiaAppendRuntimeId');
  CheckEqI(UiaAppendRuntimeId, B1, 'first element of the second control');
  Check(A2 <> B2, 'unique numbers');
  RuntimeId(First, B1, B2);
  CheckEqI(A2, B2, 'stable while the provider lives');
end;

function HitId(X, Y: Integer): string;
var
  P: TPoint;
  Hit: IRawElementProviderFragment;
begin
  P := Point(X, Y);
  Windows.ClientToScreen(Host.Handle, P);
  Hit := nil;
  (GetAutomationProvider(Host) as IRawElementProviderFragmentRoot).ElementProviderFromPoint(P.X + 0.25, P.Y + 0.25,
    Hit);
  Result := IdOf(Hit);
end;

procedure TestHitTest;
var
  Focus: IRawElementProviderFragment;
begin
  Section('hit testing and focus (UIF-1423)');
  CheckEqS('First', HitId(15, 15), 'a point in First');
  CheckEqS('Last', HitId(60, 70), 'Last and Second overlap: the topmost (Last)');
  CheckEqS('Second', HitId(15, 55), 'a point only in Second');
  CheckEqS('Host', HitId(85, 15), 'a bevel is no element: the window itself');
  CheckEqS('Host', HitId(190, 15), 'a hidden label: the window itself');
  CheckEqS('Host', HitId(390, 190), 'no graphic child: the window itself');
  Focus := nil;
  CheckEqS('S_OK', ResultWord((GetAutomationProvider(Host) as IRawElementProviderFragmentRoot).GetFocus(Focus)),
    'GetFocus');
  Check(Focus = nil, 'GetFocus: nil');
end;

{ runs last, on a form of its own: an answer to a WM_GETOBJECT that no client collects stays with the system for a
  while (as any WM_GETOBJECT result), so the provider count is not compared after it }
procedure TestGetObject;
var
  Count: Integer;
  F2: TForm;
  Panel2: TPanel;
  E2: TEdit;
  Kept: IRawElementProviderSimple;
  V: TVariantArg;
begin
  Section('WM_GETOBJECT (UIF-1422, UIF-1426)');
  F2 := TForm.CreateNew(nil);
  try
    Panel2 := TPanel.Create(F2);
    Panel2.Name := 'Other';
    Panel2.Parent := F2;
    E2 := TEdit.Create(F2);
    E2.Parent := F2;
    F2.HandleNeeded;
    Count := AutomationProviderCount;
    SendMessageW(Panel2.Handle, WM_GETOBJECT, 0, LPARAM(DWORD(OBJID_CLIENT)));
    CheckEqI(Count, AutomationProviderCount, 'another object id: no provider (the default handling)');
    Check(SendMessageW(Panel2.Handle, WM_GETOBJECT, 0, LPARAM(DWORD(UiaRootObjectId))) <> 0,
      'UiaRootObjectId: answered');
    CheckEqI(Count + 1, AutomationProviderCount, 'UiaRootObjectId: the provider is created');
    Check(Panel2.AutomationObject <> nil, 'the control keeps it');
    SendMessageW(Panel2.Handle, WM_GETOBJECT, 0, LPARAM(DWORD(UiaRootObjectId)));
    CheckEqI(Count + 1, AutomationProviderCount, 'a second request: the same provider');
    SendMessageW(E2.Handle, WM_GETOBJECT, 0, LPARAM(DWORD(UiaRootObjectId)));
    CheckEqI(Count + 1, AutomationProviderCount, 'a native control gets no provider');
    Kept := GetAutomationProvider(Panel2);
    Panel2.Free;
    FillChar(V, SizeOf(V), 0);
    CheckEqS('UIA_E_ELEMENTNOTAVAILABLE', ResultWord(Kept.GetPropertyValue(UIA_NamePropertyId, V)),
      'its window destroyed: the provider is disconnected');
    Kept := nil;
  finally
    F2.Free;
  end;
end;

procedure TestLifetime;
var
  Gone: IRawElementProviderSimple;
  Frag, Next: IRawElementProviderFragment;
  V: TVariantArg;
  Moved: TLabel;
  Old: IRawElementProviderSimple;
  W: TPanel;
  Count: Integer;
begin
  Section('lifetime (UIF-1425, UIF-1426, UIF-1427)');
  Moved := TLabel(Add(TLabel, Host, 'Moved', 300, 150, 40, 20));
  Gone := GetAutomationProvider(Moved);
  Moved.Free;
  FillChar(V, SizeOf(V), 0);
  CheckEqS('UIA_E_ELEMENTNOTAVAILABLE', ResultWord(Gone.GetPropertyValue(UIA_NamePropertyId, V)),
    'a destroyed label: GetPropertyValue');
  Frag := Gone as IRawElementProviderFragment;
  Next := nil;
  CheckEqS('UIA_E_ELEMENTNOTAVAILABLE', ResultWord(Frag.Navigate(NavigateDirection_Parent, Next)),
    'a destroyed label: Navigate');
  Frag := nil;
  Gone := nil;
  Moved := TLabel(Add(TLabel, Host, 'Removed', 300, 150, 40, 20));
  Old := GetAutomationProvider(Moved);
  Moved.Parent := nil;
  FillChar(V, SizeOf(V), 0);
  CheckEqS('UIA_E_ELEMENTNOTAVAILABLE', ResultWord(Old.GetPropertyValue(UIA_NamePropertyId, V)),
    'removed from its parent: the old provider is disconnected');
  Moved.Parent := Host;
  Check(not SameObject(Old, GetAutomationProvider(Moved)), 'back in a parent: a new provider');
  CheckEqS('Removed', PropOf(Moved, UIA_AutomationIdPropertyId), 'the new provider answers');
  Old := nil;
  Moved.Free;
  W := TPanel(Add(TPanel, F, 'Recreated', 420, 100, 50, 50));
  W.HandleNeeded;
  Old := GetAutomationProvider(W);
  W.Parent := nil;                                            { its window is destroyed }
  FillChar(V, SizeOf(V), 0);
  CheckEqS('UIA_E_ELEMENTNOTAVAILABLE', ResultWord(Old.GetPropertyValue(UIA_NamePropertyId, V)),
    'its window destroyed: the old provider answers no more');
  Old := nil;
  W.Free;
  Count := AutomationProviderCount;
  First.AccessibleName := 'Renamed';
  Edit.AccessibleName := 'Renamed edit';
  W := TPanel(Add(TPanel, F, 'Quiet', 420, 100, 50, 50));
  W.AccessibleName := 'changed';
  W.Caption := 'changed too';
  CheckEqI(Count, AutomationProviderCount, 'changes of controls without a provider create none (UIF-1440)');
  CheckEqS('Renamed', PropOf(First, UIA_NamePropertyId), 'the changed name is answered');
  W.Free;
end;

procedure TestRegistry;
var
  L1, L2: TLabel;
begin
  Section('registry (UIF-1421)');
  RegisterAutomationProvider(TTestLabel, TTestLabelProvider);
  L1 := TLabel(Add(TTestSubLabel, Host, 'SubLabel', 300, 100, 40, 20));
  L2 := TLabel(Add(TLabel, Host, 'PlainLabel', 340, 100, 40, 20));
  L1.Caption := 'caption';
  L2.Caption := 'caption';
  Check(AutomationProviderClassOf(L1) = TTestLabelProvider, 'the nearest registered ancestor');
  CheckEqS('registered', PropOf(L1, UIA_NamePropertyId), 'its own text from the registered class');
  CheckEqS('Slider', ControlTypeOf(L1), 'its control type from the registered class');
  CheckEqS('caption', PropOf(L2, UIA_NamePropertyId), 'TLabel keeps its provider class');
  CheckEqS('Text', ControlTypeOf(L2), 'TLabel keeps its control type');
  Check(AutomationProviderClassOf(Edit).InheritsFrom(TWindowAutomationProvider), 'TWinControl: a window provider');
  Check(AutomationProviderClassOf(Bevel).InheritsFrom(TGraphicAutomationProvider), 'TGraphicControl: a fragment provider');
  L1.Free;
  L2.Free;
end;

begin
  Application.Initialize;
  Build;
  try
    TestRootAndHost;
    TestNavigation;
    TestFragments;
    TestRuntimeIds;
    TestHitTest;
    TestLifetime;
    TestRegistry;
  finally
    F.Free;
  end;
  CheckEqI(0, AutomationProviderCount, 'every provider object is released (UIF-1425)');
  TestGetObject;
  Done;
end.
