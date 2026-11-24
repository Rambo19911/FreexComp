// covers: LANG-564
program InlineForVarManaged;

{ The element variable of for var … in and the interface-typed enumerator are released when the loop is left, also by Break. }

type
  IItem = interface
    function Name: string;
  end;

  TItem = class(TInterfacedObject, IItem)
  private
    FName: string;
  public
    constructor Create(const AName: string);
    destructor Destroy; override;
    function Name: string;
  end;

  IItemEnumerator = interface
    function MoveNext: Boolean;
    function GetCurrent: IItem;
    property Current: IItem read GetCurrent;
  end;

  IItems = interface
    function GetEnumerator: IItemEnumerator;
  end;

  TItemEnumerator = class(TInterfacedObject, IItemEnumerator)
  private
    FIndex, FCount: Integer;
  public
    constructor Create(ACount: Integer);
    destructor Destroy; override;
    function MoveNext: Boolean;
    function GetCurrent: IItem;
  end;

  TItems = class(TInterfacedObject, IItems)
  public
    destructor Destroy; override;
    function GetEnumerator: IItemEnumerator;
  end;

constructor TItem.Create(const AName: string);
begin
  inherited Create;
  FName := AName;
end;

destructor TItem.Destroy;
begin
  Writeln('destroy ', FName);
  inherited Destroy;
end;

function TItem.Name: string;
begin
  Result := FName;
end;

constructor TItemEnumerator.Create(ACount: Integer);
begin
  inherited Create;
  FCount := ACount;
end;

destructor TItemEnumerator.Destroy;
begin
  Writeln('destroy enumerator');
  inherited Destroy;
end;

function TItemEnumerator.MoveNext: Boolean;
begin
  Inc(FIndex);
  Result := FIndex <= FCount;
end;

function TItemEnumerator.GetCurrent: IItem;
begin
  if FIndex = 1 then
    Result := TItem.Create('item one')
  else if FIndex = 2 then
    Result := TItem.Create('item two')
  else
    Result := TItem.Create('item three');
end;

destructor TItems.Destroy;
begin
  Writeln('destroy items');
  inherited Destroy;
end;

function TItems.GetEnumerator: IItemEnumerator;
begin
  Result := TItemEnumerator.Create(3);
end;

procedure Strings;
var
  Before: NativeInt;
  Words: array of string;
begin
  SetLength(Words, 3);
  Words[0] := 'red';
  Words[1] := 'green';
  Words[2] := 'blue';
  Before := _LiveAllocCount;
  for var S in Words do
  begin
    var Twice := S + S;
    Writeln(Twice);
  end;
  Writeln('leaked: ', _LiveAllocCount - Before);
  for var S in Words do
  begin
    var Long := S + '.' + S;
    if S = 'green' then
      Break;
    Writeln(Long);
  end;
  Writeln('leaked after break: ', _LiveAllocCount - Before);
end;

procedure Interfaces(const Items: IItems);
begin
  for var I in Items do
    Writeln(I.Name);
  Writeln('after full loop');
  for var I in Items do
  begin
    Writeln(I.Name);
    if I.Name = 'item two' then
      Break;
  end;
  Writeln('after break');
end;

procedure Classic(const Items: IItems);
var
  I: IItem;
begin
  for I in Items do
    if I.Name = 'item one' then
      Break;
  Writeln('after classic loop');
end;

var
  Items: IItems;
begin
  Strings;
  Items := TItems.Create;
  Interfaces(Items);
  Classic(Items);
  Writeln('end of Classic');
  Items := nil;
  Writeln('done');
end.
