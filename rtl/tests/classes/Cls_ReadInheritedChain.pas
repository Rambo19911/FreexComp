// phase: 8
// covers: RTL-1125, RTL-1126, RTL-1127, RTL-1129
program Cls_ReadInheritedChain;
{ Visual form inheritance in the form reader (task R-248, ui-framework/phase8/form-inheritance.md section 3): three
  classes TCls1 -> TCls2 -> TCls3 each embed a form file; InitInheritedComponent reads them base-most first into one
  instance. The root of a file that starts with "inherited" is the open form scope: a nested "inherited" object is
  looked up among the components the earlier levels created, anywhere in the root (RTL-1125, 1127), and new
  "object" components may be added inside the root and inside its override blocks (RTL-1126). Only the last level
  applies its header name, and every component of the merged tree gets exactly one Loaded at the end of the last
  level, children before parents, the root last (RTL-1129). The component class TLeaf is not registered: the reader
  takes it from a published field of an ancestor class (RTL-1105). }
uses
  SysUtils, Classes, TestKit;

var
  LoadedLog: string;
  ProbeLog: string;
  HandlerLog: string;

type
  TNode = class(TComponent)
  private
    FCaption: string;
    FSize: Integer;
    FBuddy: TComponent;
    FOnPing: TNotifyEvent;
    procedure SetProbe(Value: Integer);
    function GetProbe: Integer;
  public
    ParentComp: TComponent;
    Kids: TList;
    LoadedCount: Integer;
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure SetParentComponent(Value: TComponent); override;
    procedure Loaded; override;
  published
    property Caption: string read FCaption write FCaption;
    property Size: Integer read FSize write FSize;
    property Buddy: TComponent read FBuddy write FBuddy;
    property OnPing: TNotifyEvent read FOnPing write FOnPing;
    property Probe: Integer read GetProbe write SetProbe;
  end;

  TPart = class(TNode)
  end;

  { not registered: found through the published field Extra of TCls1 }
  TLeaf = class(TNode)
  end;

  { a class without a form: RootAncestor of the chain }
  TClsBase = class(TNode)
  end;

  TCls1 = class(TClsBase)
  published
    Panel: TPart;
    A: TPart;
    B: TPart;
    Side: TPart;
    Extra: TLeaf;
    procedure PingHandler(Sender: TObject);
  end;

  TCls2 = class(TCls1)
  published
    C: TPart;
  end;

  TCls3 = class(TCls2)
  published
    D: TPart;
    procedure PingHandler(Sender: TObject);
  end;

{$FORM Cls_ReadInheritedChain1.form}
{$FORM Cls_ReadInheritedChain2.form}
{$FORM Cls_ReadInheritedChain3.form}

constructor TNode.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  Kids := TList.Create;
end;

destructor TNode.Destroy;
begin
  Kids.Free;
  inherited Destroy;
end;

procedure TNode.SetParentComponent(Value: TComponent);
begin
  ParentComp := Value;
  if Value is TNode then
    TNode(Value).Kids.Add(Pointer(Self));
end;

procedure TNode.Loaded;
begin
  inherited Loaded;
  LoadedCount := LoadedCount + 1;
  LoadedLog := LoadedLog + ' ' + Name;
end;

function TNode.GetProbe: Integer;
begin
  Result := 0;
end;

{ counts the components of the owner (the root included) that have csLoading while this line is read }
procedure TNode.SetProbe(Value: Integer);
var
  I, N, Total: Integer;
begin
  Total := Owner.ComponentCount + 1;
  N := 0;
  if csLoading in Owner.ComponentState then
    N := N + 1;
  for I := 0 to Owner.ComponentCount - 1 do
    if csLoading in Owner.Components[I].ComponentState then
      N := N + 1;
  ProbeLog := IntToStr(N) + ' of ' + IntToStr(Total) + ' loading';
end;

procedure TCls1.PingHandler(Sender: TObject);
begin
  HandlerLog := HandlerLog + ' level1(' + TComponent(Sender).Name + ')';
end;

procedure TCls3.PingHandler(Sender: TObject);
begin
  HandlerLog := HandlerLog + ' level3(' + TComponent(Sender).Name + ')';
  inherited;
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

procedure DumpOne(C: TComponent; const Indent: string);
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
      IntToStr(N.Size) + ' buddy=' + NameOf(N.Buddy) + ' loaded=' + IntToStr(N.LoadedCount) + ' kids=[';
    for I := 0 to N.Kids.Count - 1 do
    begin
      if I > 0 then
        Line := Line + ',';
      Line := Line + NameOf(TComponent(N.Kids[I]));
    end;
    Line := Line + ']';
  end;
  if csLoading in C.ComponentState then
    Line := Line + ' loading';
  if csReading in C.ComponentState then
    Line := Line + ' reading';
  WriteLn(Line);
end;

procedure Dump(Root: TComponent);
var
  I: Integer;
begin
  DumpOne(Root, '');
  for I := 0 to Root.ComponentCount - 1 do
    DumpOne(Root.Components[I], '  ');
end;

var
  Holder, Twin: TComponent;
  One: TCls1;
  Two: TCls2;
  Three: TCls3;
  E: TNotifyEvent;
  M: TMethod;
begin
  RegisterClasses([TPart]);

  Section('one level behaves as before (RTL-1129 (c))');
  LoadedLog := '';
  One := TCls1.Create(nil);
  CheckEqB(True, InitInheritedComponent(One, TClsBase), 'found');
  Dump(One);
  WriteLn('Loaded order:', LoadedLog);
  One.Free;

  Section('two levels');
  LoadedLog := '';
  Two := TCls2.Create(nil);
  CheckEqB(True, InitInheritedComponent(Two, TClsBase), 'found');
  Dump(Two);
  WriteLn('Loaded order:', LoadedLog);
  Two.Free;

  Section('three levels (RTL-1125, 1126, 1127, 1129)');
  Holder := TComponent.Create(nil);
  Twin := TComponent.Create(Holder);
  Twin.Name := 'Cls1Form';
  LoadedLog := '';
  ProbeLog := '';
  Three := TCls3.Create(Holder);
  CheckEqB(True, InitInheritedComponent(Three, TClsBase), 'found');
  Dump(Three);
  WriteLn('Loaded order:', LoadedLog);
  WriteLn('while level 3 reads: ', ProbeLog);
  CheckEqS('Cls3Form', Three.Name, 'the name is the header name of the last level');
  CheckEqI(2, Holder.ComponentCount, 'the owner holds the level-1 header name and the instance');
  Check((Three.Panel <> nil) and (Three.A <> nil) and (Three.B <> nil) and (Three.Side <> nil), 'level-1 fields bound');
  Check((Three.C <> nil) and (Three.Extra <> nil) and (Three.D <> nil), 'fields of new components bound');
  CheckEqS('TLeaf', Three.Extra.ClassName, 'unregistered class from an ancestor''s field (RTL-1105)');
  Check(Three.A.ParentComp = Three.Panel, 'a block not nested as in the ancestor keeps the real parent');
  Check(Three.A.Buddy = Three.Side, 'a reference of level 3 to a level-1 component');
  Check(Three.D.Buddy = Three.Extra, 'a reference of level 3 to a level-2 component');
  Check(Three.Side.Buddy = Three, 'level 1''s own header name names the root');

  Section('an event line of level 1 binds the most derived method (RTL-1127)');
  E := Three.A.OnPing;
  M := TMethod(E);
  Check(M.Data = Pointer(Three), 'bound to the root');
  Check(M.Code = Three.MethodAddress('PingHandler'), 'the method of the most derived class');
  HandlerLog := '';
  E(Three.A);
  WriteLn('handlers:', HandlerLog);
  Holder.Free;
  Done;
end.
