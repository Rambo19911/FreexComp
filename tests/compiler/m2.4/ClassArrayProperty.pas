// covers: LANG-642
program ClassArrayProperty;

{ An indexed property with getter and setter that is the default property (L[i] = L.Items[i]), used qualified, through
  the default, as an assignment target and inside a method without qualifier; a two-index property; a property indexed
  by a string. }

type
  TList = class
  private
    FItems: array[0..9] of Integer;
    FCount: Integer;
    function GetItem(I: Integer): Integer;
    procedure SetItem(I: Integer; const V: Integer);
    function GetCell(R, C: Integer): Integer;
    function GetNamed(const S: string): Integer;
  public
    procedure Add(V: Integer);
    property Count: Integer read FCount;
    property Items[I: Integer]: Integer read GetItem write SetItem; default;
    property Cells[R, C: Integer]: Integer read GetCell;
    property Named[const S: string]: Integer read GetNamed;
  end;

function TList.GetItem(I: Integer): Integer;
begin
  Result := FItems[I];
end;

procedure TList.SetItem(I: Integer; const V: Integer);
begin
  FItems[I] := V;
end;

function TList.GetCell(R, C: Integer): Integer;
begin
  Result := R * 10 + C;
end;

function TList.GetNamed(const S: string): Integer;
begin
  Result := Length(S) * 10;
end;

procedure TList.Add(V: Integer);
begin
  FCount := FCount + 1;
  Items[FCount - 1] := V;
end;

var
  L: TList;
  I: Integer;
begin
  L := TList.Create;
  for I := 1 to 5 do
    L.Add(I * I);
  L.Items[0] := 100;
  L[1] := L[1] + 1;
  for I := 0 to L.Count - 1 do
    Write(' ', L[I]);
  Writeln;
  Writeln(L.Items[4], ' ', L.Cells[2, 3], ' ', L.Named['abc']);
  L.Free;
end.
