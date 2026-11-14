// covers: LANG-660, LANG-664
program IntfProperties;

{ Interface properties compile to calls of their accessor methods through the IMT: a read/write property, a
  read-only one, an inherited property used through a descendant interface, an array property that is the default,
  and index properties sharing one getter. }

type
  ICounter = interface
    ['{C0C0C0C0-0000-0000-0000-000000000001}']
    function GetValue: Integer;
    procedure SetValue(V: Integer);
    function GetName: string;
    property Value: Integer read GetValue write SetValue;
    property Name: string read GetName;
  end;

  IList = interface(ICounter)
    ['{C0C0C0C0-0000-0000-0000-000000000002}']
    function GetItem(I: Integer): string;
    procedure SetItem(I: Integer; const S: string);
    function GetFlag(Index: Integer): Boolean;
    property Items[I: Integer]: string read GetItem write SetItem; default;
    property First: Boolean index 0 read GetFlag;
    property Third: Boolean index 2 read GetFlag;
  end;

  TList = class(TInterfacedObject, IList)
  private
    FValue: Integer;
    FItems: array[0..2] of string;
  public
    function GetValue: Integer;
    procedure SetValue(V: Integer);
    function GetName: string;
    function GetItem(I: Integer): string;
    procedure SetItem(I: Integer; const S: string);
    function GetFlag(Index: Integer): Boolean;
  end;

function TList.GetValue: Integer;
begin
  Result := FValue;
end;

procedure TList.SetValue(V: Integer);
begin
  Writeln('set ', V);
  FValue := V;
end;

function TList.GetName: string;
begin
  Result := 'list';
end;

function TList.GetItem(I: Integer): string;
begin
  Result := FItems[I];
end;

procedure TList.SetItem(I: Integer; const S: string);
begin
  FItems[I] := S;
end;

function TList.GetFlag(Index: Integer): Boolean;
begin
  Result := FItems[Index] <> '';
end;

var
  L: IList;
  C: ICounter;
begin
  L := TList.Create;
  L.Value := 5;
  L.Value := L.Value + 2;
  Writeln(L.Value, ' ', L.Name);
  L.Items[0] := 'a';
  L[1] := 'b';
  Writeln(L.Items[0], L[1], '|', L[2], '|');
  Writeln(L.First, ' ', L.Third);
  C := L;
  C.Value := 10;
  Writeln(C.Value, ' ', L.Value);
end.
