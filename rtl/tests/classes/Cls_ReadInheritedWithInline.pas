// phase: 8
// covers: RTL-1126, RTL-1134
program Cls_ReadInheritedWithInline;
{ Form inheritance together with frames (task R-248, ui-framework/phase8/form-inheritance.md sections 3 and 6): the
  level-1 form of TInl1 holds an instance F of the test frame of Cls_ReadInline with an override of a frame child;
  the level-2 form of TInl2 changes the instance with an "inherited" block (nothing is created; the instance is a
  closed scope, so its nested blocks name frame children) and overrides the same frame child again: the lines apply
  in order frame, level 1, level 2. The frame child gets Loaded from the frame's reader and once from the chain, the
  instance and the root once (RTL-1129). An "object" inside the block of an inherited instance is F0110 (RTL-1126).
  TTestFrameEx derives from TTestFrame with a form of its own: its instances merge both levels, apply no header
  name when they have an owner and get one Loaded for their root (RTL-1134). }
uses
  SysUtils, Classes, TestKit;

var
  Live: Integer;

type
  TNode = class(TComponent)
  private
    FCaption: string;
    FSize: Integer;
    FBuddy: TComponent;
  public
    ParentComp: TComponent;
    LoadedCount: Integer;
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure SetParentComponent(Value: TComponent); override;
    procedure Loaded; override;
  published
    property Caption: string read FCaption write FCaption;
    property Size: Integer read FSize write FSize;
    property Buddy: TComponent read FBuddy write FBuddy;
  end;

  TPart = class(TNode)
  end;

  { stands for TFrame: a class without a resource of its own }
  TTestBase = class(TNode)
  end;

  TTestFrame = class(TTestBase)
  published
    Title: TPart;
    Box: TPart;
    Inner: TPart;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TTestFrameEx = class(TTestFrame)
  published
    Extra: TPart;
  end;

  { a class without a form: RootAncestor of the forms }
  TInlBase = class(TNode)
  end;

  TInl1 = class(TInlBase)
  published
    F: TTestFrame;
  end;

  TInl2 = class(TInl1)
  published
    G: TTestFrameEx;
  end;

  TInlBad = class(TInl1)
  end;

{$FORM Cls_ReadInlineFrame.form}
{$FORM Cls_ReadInheritedWithInlineEx.form}
{$FORM Cls_ReadInheritedWithInline1.form}
{$FORM Cls_ReadInheritedWithInline2.form}
{$FORM Cls_ReadInheritedWithInlineBad.form}

constructor TNode.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  Live := Live + 1;
end;

destructor TNode.Destroy;
begin
  Live := Live - 1;
  inherited Destroy;
end;

procedure TNode.SetParentComponent(Value: TComponent);
begin
  ParentComp := Value;
end;

procedure TNode.Loaded;
begin
  inherited Loaded;
  LoadedCount := LoadedCount + 1;
end;

constructor TTestFrame.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  InitInheritedComponent(Self, TTestBase, AOwner = nil);
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

function QualName(C: TComponent): string;
begin
  if C = nil then
    Result := 'nil'
  else if C.Owner = nil then
    Result := NameOf(C)
  else
    Result := NameOf(C.Owner) + '.' + NameOf(C);
end;

{ one line per component, then the components it owns, indented }
procedure Dump(C: TComponent; const Indent: string);
var
  I: Integer;
  N: TNode;
  Line: string;
begin
  Line := Indent + NameOf(C) + ': ' + C.ClassName + ' owner=' + NameOf(C.Owner);
  if C is TNode then
  begin
    N := TNode(C);
    Line := Line + ' parent=' + NameOf(N.ParentComp) + ' caption=' + QuoteStr(N.Caption) + ' size=' +
      IntToStr(N.Size) + ' buddy=' + QualName(N.Buddy) + ' loaded=' + IntToStr(N.LoadedCount);
  end;
  if csInline in C.ComponentState then
    Line := Line + ' inline';
  if csLoading in C.ComponentState then
    Line := Line + ' loading';
  if csReading in C.ComponentState then
    Line := Line + ' reading';
  WriteLn(Line);
  for I := 0 to C.ComponentCount - 1 do
    Dump(C.Components[I], Indent + '  ');
end;

var
  Alone, X, Y: TTestFrameEx;
  Inst: TInl2;
  Bad: TInlBad;
  Msg: string;
begin
  RegisterClasses([TPart, TTestFrame, TTestFrameEx]);

  Section('a frame derived from a frame (RTL-1134)');
  Alone := TTestFrameEx.Create(nil);
  Dump(Alone, '');
  Alone.Free;
  Inst := TInl2.Create(nil);
  X := TTestFrameEx.Create(Inst);
  Y := TTestFrameEx.Create(Inst);
  CheckEqS('|', X.Name + '|' + Y.Name, 'with an owner no level applies its header name');
  CheckEqS('title ex|extra', X.Title.Caption + '|' + X.Extra.Caption, 'both levels read');
  CheckEqI(1, X.LoadedCount, 'the root''s Loaded once');
  Inst.Free;
  CheckEqI(0, Live, 'freed');

  Section('an inherited form with a frame instance (RTL-1126, 1129)');
  Inst := TInl2.Create(nil);
  CheckEqB(True, InitInheritedComponent(Inst, TInlBase), 'found');
  Dump(Inst, '');
  Check(Inst.F.Inner.ParentComp = Inst.F.Box, 'the frame child keeps the parent the frame gave');
  Check(Inst.F.Owner = Inst, 'the inherited instance is still owned by the root');
  CheckEqI(2, Inst.ComponentCount, 'nothing created by the block of the instance');
  Inst.Free;
  CheckEqI(0, Live, 'freed');

  Section('nothing can be added inside an inherited instance (RTL-1126)');
  Bad := TInlBad.Create(nil);
  Msg := '';
  try
    InitInheritedComponent(Bad, TInlBase);
  except
    on E: EReadError do
      Msg := E.Message;
  end;
  CheckEqS('TInlBad.form(3,5): error F0110: Components cannot be added to frame instance F', Msg, 'F0110');
  CheckEqI(1, Bad.ComponentCount, 'only the level-1 instance is left');
  Check(not (csLoading in Bad.F.ComponentState) and not (csReading in Bad.F.ComponentState),
    'the level-1 instance has no loading flags');
  Check(not (csLoading in Bad.ComponentState) and not (csReading in Bad.ComponentState), 'nor the root');
  CheckEqI(0, Bad.LoadedCount + Bad.F.LoadedCount, 'no Loaded from the chain');
  Bad.Free;
  CheckEqI(0, Live, 'nothing leaked');
  Done;
end.
