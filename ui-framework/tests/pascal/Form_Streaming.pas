// covers: UIF-300, UIF-301, UIF-430, UIF-450, UIF-451
// needs: desktop
program Form_Streaming;
{ U-042, application-forms.md 4.4 and 7, layout.md 12: a form class with an embedded .form is read by its
  constructor (InitInheritedComponent): properties, child controls parented through SetParentComponent, published
  fields and event bindings. Load-time sequence: every Loaded (children first) runs before the first layout pass,
  baselines come from the stored bounds, then the root realigns parents before children; no OnResize fires during
  loading; OnCreate follows the layout; no window handle is created. A reader error propagates as EReadError out
  of the constructor and CreateForm; a class without form data raises EResNotFound. GetChildren (UIF-450) yields the
  owned components without a parent first, in owner order, then the form's child controls owned by the form in
  z-order. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, Menus, TestKit;

type
  TBox = class(TCustomControl)
  protected
    procedure Loaded; override;
    procedure AlignControls(AControl: TControl; var Rect: TRect); override;
  published
    property OnClick;
    property OnResize;
  end;

  TStreamForm = class(TForm)
  protected
    procedure Loaded; override;
    procedure AlignControls(AControl: TControl; var Rect: TRect); override;
  private
    FChildren: string;
    procedure Collect(Child: TComponent);
  public
    function ChildNames: string;
  published
    Header: TBox;
    Inner: TBox;
    Corner: TBox;
    Body: TBox;
    procedure FormCreate(Sender: TObject);
    procedure FormResize(Sender: TObject);
    procedure HeaderClick(Sender: TObject);
    procedure BoxResize(Sender: TObject);
  end;

  TBadForm = class(TForm)
  end;

  TNoDataForm = class(TForm)
  end;

{$FORM Form_Streaming.form}
{$FORM Form_StreamingBad.form}

var
  Log: string;
  CreateBounds: string;
  BadRef: TBadForm;

function BoundsText(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

procedure TBox.Loaded;
begin
  Log := Log + 'loaded:' + Name + ';';
  inherited Loaded;
end;

procedure TBox.AlignControls(AControl: TControl; var Rect: TRect);
begin
  Log := Log + 'align:' + Name + ';';
  inherited AlignControls(AControl, Rect);
end;

procedure TStreamForm.Loaded;
begin
  Log := Log + 'loaded:' + Name + ';';
  inherited Loaded;
end;

procedure TStreamForm.AlignControls(AControl: TControl; var Rect: TRect);
begin
  Log := Log + 'align:' + Name + ';';
  inherited AlignControls(AControl, Rect);
end;

procedure TStreamForm.Collect(Child: TComponent);
begin
  FChildren := FChildren + Child.Name + ';';
end;

function TStreamForm.ChildNames: string;
begin
  FChildren := '';
  GetChildren(Collect, Self);
  Result := FChildren;
end;

procedure TStreamForm.FormCreate(Sender: TObject);
begin
  Log := Log + 'create;';
  CreateBounds := BoundsText(Header) + ' ' + BoundsText(Inner) + ' ' + BoundsText(Body);
end;

procedure TStreamForm.FormResize(Sender: TObject);
begin
  Log := Log + 'formresize;';
end;

procedure TStreamForm.HeaderClick(Sender: TObject);
begin
  Log := Log + 'click;';
end;

procedure TStreamForm.BoxResize(Sender: TObject);
begin
  Log := Log + 'resize:' + TComponent(Sender).Name + ';';
end;

function FirstPos(const Sub, S: string): Integer;
begin
  Result := Pos(Sub, S);
end;

function LastPos(const Sub, S: string): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := Length(S) - Length(Sub) + 1 downto 1 do
    if Copy(S, I, Length(Sub)) = Sub then
    begin
      Result := I;
      Exit;
    end;
end;

function NewComponent(AOwner: TComponent; const AName: string): TComponent;
begin
  Result := TComponent.Create(AOwner);
  Result.Name := AName;
end;

procedure Run;
var
  F: TStreamForm;
  Count: Integer;
  M: TMainMenu;
  Item: TMenuItem;
  Foreign: TBox;
begin
  Application.Initialize;

  Section('properties, children, fields, events (UIF-430 step 2)');
  Log := '';
  F := TStreamForm.Create(nil);
  try
    CheckEqS('StreamForm', F.Name, 'root name from the header');
    CheckEqS('Streamed', F.Caption, 'Caption');
    CheckEqI(300, F.ClientWidth, 'ClientWidth');
    CheckEqI(200, F.ClientHeight, 'ClientHeight');
    CheckEqB(True, (F.Header <> nil) and (F.Inner <> nil) and (F.Corner <> nil) and (F.Body <> nil),
      'published fields bound');
    CheckEqB(True, F.FindComponent('Inner') = F.Inner, 'field is the named component');
    CheckEqB(True, (F.Header.Owner = F) and (F.Inner.Owner = F), 'children owned by the form');
    CheckEqB(True, F.Header.Parent = F, 'SetParentComponent: Header on the form');
    CheckEqB(True, F.Inner.Parent = F.Header, 'SetParentComponent: Inner on Header');
    CheckEqI(3, F.ControlCount, 'three controls on the form');
    CheckEqB(True, (F.Controls[0] = F.Header) and (F.Controls[1] = F.Corner) and (F.Controls[2] = F.Body),
      'creation order');
    CheckEqB(True, F.Header.Align = alTop, 'Align read');
    CheckEqB(True, F.Inner.Anchors = [akLeft, akTop, akRight], 'Anchors read');
    CheckEqB(False, csLoading in F.ComponentState, 'form loaded');
    CheckEqB(False, (csLoading in F.Header.ComponentState) or (csLoading in F.Inner.ComponentState),
      'children loaded');
    Log := '';
    F.Header.OnClick(F.Header);
    CheckEqS('click;', Log, 'event bound to the form method');
    CheckEqB(False, F.HandleAllocated or F.Header.HandleAllocated, 'no window handle created');
  finally
    F.Free;
  end;

  Section('load-time sequence (layout.md 12)');
  Log := '';
  F := TStreamForm.Create(nil);
  try
    Check(FirstPos('loaded:StreamForm;', Log) > 0, 'the form''s Loaded runs');
    { CreateNew runs one pass on the still empty, unnamed form ('align:;') before the reader starts }
    CheckEqI(0, Pos('align:', Copy(Log, FirstPos('loaded:', Log), LastPos('loaded:', Log) - FirstPos('loaded:', Log))),
      'every Loaded before the first layout pass');
    Check(FirstPos('loaded:Inner;', Log) < FirstPos('loaded:Header;', Log), 'children loaded before parents');
    Check(LastPos('loaded:', Log) = FirstPos('loaded:StreamForm;', Log), 'the form is loaded last');
    Check(FirstPos('align:StreamForm;', Log) < FirstPos('align:Header;', Log), 'root pass first');
    Check(FirstPos('align:Header;', Log) < FirstPos('align:Inner;', Log), 'parents before children');
    Check((FirstPos('align:Corner;', Log) > 0) and (FirstPos('align:Body;', Log) > 0),
      'every windowed control runs a pass');
    CheckEqI(0, FirstPos('resize:', Log), 'no OnResize of a child during loading');
    CheckEqI(0, FirstPos('formresize;', Log), 'no OnResize of the form during loading');
    Check(LastPos('align:', Log) < FirstPos('create;', Log), 'OnCreate after the layout');
    CheckEqS('0,0,300,40', BoundsText(F.Header), 'alTop: inconsistent stored bounds are moved (UIF-301)');
    CheckEqS('10,5,70,20', BoundsText(F.Inner), 'anchor baseline from the stored bounds, then layout');
    CheckEqS('0,40,300,160', BoundsText(F.Body), 'alClient');
    CheckEqS('250,150,40,30', BoundsText(F.Corner), 'anchored control not moved');
    CheckEqS('0,0,300,40 10,5,70,20 0,40,300,160', CreateBounds, 'OnCreate sees the final layout');
    Log := '';
    F.ClientWidth := 400;
    F.ClientHeight := 250;
    CheckEqS('350,200,40,30', BoundsText(F.Corner), 'Corner baseline is the stored form client size');
    CheckEqS('10,5,170,20', BoundsText(F.Inner), 'Inner follows its parent');
    Check(FirstPos('resize:Header;', Log) > 0, 'after loading, OnResize fires again');
  finally
    F.Free;
  end;

  Section('GetChildren (UIF-450)');
  F := TStreamForm.Create(nil);
  try
    CheckEqS('Header;Corner;Body;', F.ChildNames, 'loaded form: its controls in z-order');
    NewComponent(F, 'First');
    M := TMainMenu.Create(F);
    M.Name := 'Menu';
    Item := TMenuItem.Create(F);
    Item.Name := 'Item';
    M.Items.Add(Item);
    NewComponent(F, 'Second');
    Foreign := TBox.Create(nil);
    Foreign.Name := 'Foreign';
    Foreign.Parent := F;
    F.Body.SendToBack;
    CheckEqS('First;Menu;Second;Body;Header;Corner;', F.ChildNames,
      'owned components without a parent in owner order, then owned controls in z-order');
    Foreign.Free;
  finally
    F.Free;
  end;

  Section('errors (UIF-430, application-forms.md 7)');
  Count := Screen.FormCount;
  Expect('EReadError', 'TBadForm.form(3,3): error F0102: Property Bogus does not exist on TBadForm');
  try
    TBadForm.Create(nil);
    CheckRaises('reader error out of the constructor', '', '');
  except
    on E: Exception do
      CheckRaises('reader error out of the constructor', E.ClassName, E.Message);
  end;
  CheckEqI(Count, Screen.FormCount, 'the failed form is destroyed');
  Expect('EReadError', 'TBadForm.form(3,3): error F0102: Property Bogus does not exist on TBadForm');
  try
    Application.CreateForm(TBadForm, BadRef);
    CheckRaises('reader error out of CreateForm', '', '');
  except
    on E: Exception do
      CheckRaises('reader error out of CreateForm', E.ClassName, E.Message);
  end;
  CheckEqB(True, BadRef = nil, 'CreateForm reference nil');
  CheckEqB(True, Application.MainForm = nil, 'no main form');
  Expect('EResNotFound', 'Resource TNoDataForm not found');
  try
    TNoDataForm.Create(nil);
    CheckRaises('class without form data', '', '');
  except
    on E: Exception do
      CheckRaises('class without form data', E.ClassName, E.Message);
  end;
  CheckEqI(Count, Screen.FormCount, 'no form left registered');
end;

begin
  RegisterClass(TBox);
  Run;
  Done;
end.
