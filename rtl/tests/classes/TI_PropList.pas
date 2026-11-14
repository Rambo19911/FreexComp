// phase: 4
// covers: RTL-401, RTL-402
program TI_PropList;
{ GetPropInfo / FindPropInfo / IsPublishedProp / GetPropList over published properties (classes.md section 1):
  lookups are ASCII case-insensitive and search the class, then its ancestors; a Kinds filter yields nil for other
  kinds; public properties (present in the default broad tables with Visibility 2) are never found. GetPropList lists
  ancestor properties first, each class in declaration order, a redeclared property keeps the ancestor's position
  and points to the descendant's info (RTL-402); the filtered overload counts with a nil list and sorts by name.
  Per-property information of RTL-401: name, type, accessor kinds, index. }
uses
  SysUtils, TypInfo, TestKit;

type
  TMode = (mdOff, mdOn, mdAuto);

  {$M+}
  TBase = class
  private
    FWidth: Integer;
    FName: string;
    FMode: TMode;
    FTag: Integer;
    FVisible: Boolean;
    function GetHeight: Integer;
    procedure SetWidth(Value: Integer);
    function GetPart(Index: Integer): Integer;
  public
    property PublicOnly: Integer read FTag;
  published
    property Width: Integer read FWidth write SetWidth default 10;
    property Name: string read FName write FName;
    property Height: Integer read GetHeight;
    property Mode: TMode read FMode write FMode default mdOn;
    property Tag: Integer read FTag write FTag;
    property Left: Integer index 3 read GetPart;
    property Visible: Boolean read FVisible write FVisible stored False;
  end;
  {$M-}

  TChild = class(TBase)
  private
    FColor: Integer;
  published
    property Color: Integer read FColor write FColor;
    property Name stored False;
    property Alpha: Integer read FColor;
  end;

function TBase.GetHeight: Integer; begin Result := 5; end;
procedure TBase.SetWidth(Value: Integer); begin FWidth := Value; end;
function TBase.GetPart(Index: Integer): Integer; begin Result := Index; end;

function Names(List: PPropList; Count: Integer): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Count - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + List^[I]^.Name;
  end;
end;

var
  P, Q: PPropInfo;
  List: PPropList;
  Buf: array[0..31] of PPropInfo;
  N: Integer;
  Msg: string;
  C: TChild;
begin
  Section('GetPropInfo');
  P := GetPropInfo(TBase, 'Width');
  Check(P <> nil, 'found by class');
  CheckEqS('Width', P^.Name, 'declared casing');
  Check(GetPropInfo(TBase, 'WIDTH') = P, 'case-insensitive');
  Check(GetPropInfo(PTypeInfo(TypeInfo(TBase)), 'width') = P, 'by type info');
  Check(GetPropInfo(TChild, 'Width') = P, 'inherited');
  Check(GetPropInfo(TChild, 'Name') <> GetPropInfo(TBase, 'Name'), 'redeclared: the descendant entry first');
  Check(GetPropInfo(TBase, 'PublicOnly') = nil, 'public property not found');
  Check(GetPropInfo(TBase, 'Missing') = nil, 'missing');
  Check(GetPropInfo(TBase, 'Width', [tkInteger]) = P, 'kind filter accepts');
  Check(GetPropInfo(TBase, 'Width', [tkUString, tkEnumeration]) = nil, 'kind filter rejects');
  C := TChild.Create;
  Check(GetPropInfo(C, 'Color') <> nil, 'by instance');
  CheckEqB(True, IsPublishedProp(C, 'mode'), 'IsPublishedProp');
  CheckEqB(False, IsPublishedProp(TBase, 'PublicOnly'), 'IsPublishedProp public');
  CheckEqB(False, IsPublishedProp(TBase, 'Color'), 'IsPublishedProp descendant');
  Check(PropType(C, 'Mode') = tkEnumeration, 'PropType');
  CheckEqB(True, PropIsType(TChild, 'Name', tkUString), 'PropIsType');
  Msg := '';
  try
    FindPropInfo(C, 'Nope');
  except
    on E: EPropertyError do
      Msg := E.Message;
  end;
  CheckEqS('Property "Nope" does not exist', Msg, 'FindPropInfo raises');

  Section('per-property information (RTL-401)');
  Check(P^.PropType = TypeInfo(Integer), 'type');
  CheckEqI(1, P^.GetKind, 'field getter');
  CheckEqI(2, P^.SetKind, 'static setter');
  CheckEqI(NoIndex, P^.Index, 'no index');
  Q := GetPropInfo(TBase, 'Height');
  CheckEqI(2, Q^.GetKind, 'static getter');
  CheckEqI(0, Q^.SetKind, 'read-only');
  Check(Q^.Default = NoDefault, 'no default');
  CheckEqI(3, GetPropInfo(TBase, 'Left')^.Index, 'index');
  CheckEqB(True, IsStoredProp(C, 'Width'), 'stored by default');
  { stored/default specifiers: the compiler emits Stored = constant 1 and no Default yet (codegen.md C-112 note);
    IsStoredProp over the other stored kinds is covered by TI_GetSet with hand-built entries }

  Section('GetPropList (RTL-402)');
  N := GetPropList(PTypeInfo(TypeInfo(TBase)), List);
  CheckEqS('Width,Name,Height,Mode,Tag,Left,Visible', Names(List, N), 'declaration order');
  FreeMem(List);
  N := GetPropList(PTypeInfo(TypeInfo(TChild)), List);
  CheckEqS('Width,Name,Height,Mode,Tag,Left,Visible,Color,Alpha', Names(List, N), 'ancestors first');
  Check(List^[1] = GetPropInfo(TChild, 'Name'), 'redeclared keeps position, descendant info');
  Check(List^[1] <> GetPropInfo(TBase, 'Name'), 'not the ancestor info');
  FreeMem(List);
  N := GetPropList(C, List);
  CheckEqI(9, N, 'by instance');
  FreeMem(List);
  N := GetPropList(PTypeInfo(TypeInfo(TObject)), List);
  CheckEqI(0, N, 'TObject has none');
  Check(List = nil, 'no list allocated');
  CheckEqI(6, GetPropList(PTypeInfo(TypeInfo(TChild)), [tkInteger], nil), 'filtered count');
  N := GetPropList(PTypeInfo(TypeInfo(TChild)), [tkInteger, tkEnumeration], @Buf);
  CheckEqS('Alpha,Color,Height,Left,Mode,Tag,Visible,Width', Names(@Buf, N), 'filtered sorted');
  N := GetPropList(PTypeInfo(TypeInfo(TChild)), [tkInteger], @Buf, False);
  CheckEqS('Width,Height,Tag,Left,Color,Alpha', Names(@Buf, N), 'filtered unsorted');
  C.Free;
  Done;
end.
