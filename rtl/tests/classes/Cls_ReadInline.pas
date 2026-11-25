// phase: 8
// covers: RTL-1090, RTL-1091, RTL-1092, RTL-1093, RTL-1097, RTL-1099, RTL-1102
program Cls_ReadInline;
{ Frame instances in the form reader (task R-242, ui-framework/phase8/frames.md section 3): an "inline" object creates
  the frame through its constructor, which streams the frame's own resource (InitInheritedComponent with
  ApplyRootName = False because it has an owner, RTL-1099), gets csInline and is the scope of the "inherited" override
  blocks nested in it (RTL-1090, 1092); an override block finds the frame's component by name and class and applies
  its lines without creating, renaming or re-parenting it (RTL-1091); event lines bind methods of the file's root and
  references resolve in the instance before the root, also through a fix-up (RTL-1093); a component reached by an
  override block gets Loaded once more, the instance itself once (RTL-1097); frames created in code with an owner
  keep an empty name, so two of them coexist. }
uses
  SysUtils, Classes, TestKit;

type
  TNode = class(TComponent)
  private
    FCaption: string;
    FSize: Integer;
    FBuddy: TNode;
    FOnPing: TNotifyEvent;
  public
    ParentComp: TComponent;
    LoadedCount: Integer;
    procedure SetParentComponent(Value: TComponent); override;
    procedure Loaded; override;
  published
    property Caption: string read FCaption write FCaption;
    property Size: Integer read FSize write FSize;
    property Buddy: TNode read FBuddy write FBuddy;
    property OnPing: TNotifyEvent read FOnPing write FOnPing;
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

  TMainRoot = class(TComponent)
  published
    Inner: TPart;
    F1: TTestFrame;
    F2: TTestFrame;
    Later: TPart;
    procedure PingHandler(Sender: TObject);
  end;

{$FORM Cls_ReadInlineFrame.form}

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

procedure TMainRoot.PingHandler(Sender: TObject);
begin
  WriteLn('PingHandler of ', ClassName, ' for ', TComponent(Sender).Owner.Name, '.', TComponent(Sender).Name);
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
  WriteLn(Line);
  for I := 0 to C.ComponentCount - 1 do
    Dump(C.Components[I], Indent + '  ');
end;

const
  MainText =
    'object Main: TMainRoot'#10 +
    '  object Inner: TPart'#10 +
    '    Caption = ''root inner'''#10 +
    '  end'#10 +
    '  inline F1: TTestFrame'#10 +
    '    Size = 42'#10 +
    '    inherited Box: TPart'#10 +
    '      Buddy = Later'#10 +
    '      inherited Inner: TPart'#10 +
    '        Caption = ''overridden'''#10 +
    '        OnPing = PingHandler'#10 +
    '      end'#10 +
    '    end'#10 +
    '    inherited Title: TPart'#10 +
    '      Buddy = Inner'#10 +
    '    end'#10 +
    '  end'#10 +
    '  inline F2: TTestFrame'#10 +
    '  end'#10 +
    '  object Later: TPart'#10 +
    '  end'#10 +
    'end'#10;

var
  Main: TMainRoot;
  Alone, X, Y: TTestFrame;
  M: TMethod;
  E: TNotifyEvent;
begin
  RegisterClasses([TPart, TTestFrame, TMainRoot]);

  Section('a frame created without an owner takes its root name (RTL-1099)');
  Alone := TTestFrame.Create(nil);
  Dump(Alone, '');
  Alone.Free;

  Section('two instances of one frame (RTL-1090, 1091)');
  Main := TMainRoot.Create(nil);
  ReadComponentFromText(MainText, Main, 'Main.form');
  Dump(Main, '');
  Check(Main.F1 <> nil, 'field F1 bound');
  Check(Main.F2 <> nil, 'field F2 bound');
  Check(Main.F1.Inner.ParentComp = Main.F1.Box, 'an override block keeps the parent the frame gave');
  Check(Main.F1.Title = Main.F1.FindComponent('Title'), 'the frame field still names its child');

  Section('lines inside a scope (RTL-1093)');
  Check(Main.F1.Title.Buddy = Main.F1.Inner, 'reference resolved inside the instance before the root');
  Check(Main.F1.Box.Buddy = Main.Later, 'forward reference resolved by a fix-up in the root');
  E := Main.F1.Inner.OnPing;
  M := TMethod(E);
  Check(M.Data = Pointer(Main), 'the event is bound to the file''s root');
  Check(Assigned(Main.F2.Inner.OnPing) = False, 'the other instance has no handler');
  E(Main.F1.Inner);

  Section('frames created in code with an owner (RTL-1099)');
  X := TTestFrame.Create(Main);
  Y := TTestFrame.Create(Main);
  CheckEqS('', X.Name, 'first frame has an empty name');
  CheckEqS('', Y.Name, 'second frame has an empty name');
  Check((X.Owner = Main) and (Y.Owner = Main), 'both owned by the root');
  CheckEqS('title|title', X.Title.Caption + '|' + Y.Title.Caption, 'both streamed');
  CheckEqI(1, X.LoadedCount, 'Loaded once');
  CheckEqB(False, csInline in X.ComponentState, 'not an instance');
  Main.Free;
  Done;
end.
