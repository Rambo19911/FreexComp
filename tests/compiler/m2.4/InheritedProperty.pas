// covers: LANG-624, LANG-641, LANG-642, LANG-643, LANG-646
program InheritedProperty;

{ inherited Prop inside a method of a class that redeclares the property (C-270): read and write of the nearest
  ancestor's property - field-backed, method-backed, an array property, an index property, a class property, a
  property found two levels up, and an ancestor accessor that is virtual (called statically, as inherited calls are). }

type
  TBase = class
  private
    FValue: Integer;
    FPlain: Integer;
    FItems: array[0..3] of Integer;
    FName: string;
    class var FShared: Integer;
    procedure SetValue(V: Integer);
    function GetItem(I: Integer): Integer;
    procedure SetItem(I: Integer; const V: Integer);
    function GetPart(Index: Integer): Integer;
    procedure SetPart(Index: Integer; const V: Integer);
  protected
    function GetName: string; virtual;
    procedure SetName(const S: string); virtual;
    property Value: Integer read FValue write SetValue;
    property Plain: Integer read FPlain write FPlain;
  public
    property Items[I: Integer]: Integer read GetItem write SetItem; default;
    property First: Integer index 0 read GetPart write SetPart;
    property Second: Integer index 1 read GetPart write SetPart;
    property Name: string read GetName write SetName;
    class property Shared: Integer read FShared write FShared;
  end;

  TMiddle = class(TBase)
  end;

  TDerived = class(TMiddle)
  private
    function GetValue: Integer;
    procedure SetValue2(V: Integer);
    function GetPlain: Integer;
    procedure SetPlain(V: Integer);
    function GetItem2(I: Integer): Integer;
    procedure SetItem2(I: Integer; const V: Integer);
    function GetSecond: Integer;
    procedure SetSecond(V: Integer);
    function GetName2: string;
    procedure SetName2(const S: string);
    class function GetShared: Integer; static;
    class procedure SetShared(V: Integer); static;
  protected
    function GetName: string; override;
    procedure SetName(const S: string); override;
  public
    function Sum: Integer;
    property Value: Integer read GetValue write SetValue2;
    property Plain: Integer read GetPlain write SetPlain;
    property Items[I: Integer]: Integer read GetItem2 write SetItem2; default;
    property Second: Integer read GetSecond write SetSecond;
    property Name: string read GetName2 write SetName2;
    class property Shared: Integer read GetShared write SetShared;
  end;

procedure TBase.SetValue(V: Integer);
begin
  FValue := V;
end;

function TBase.GetItem(I: Integer): Integer;
begin
  Result := FItems[I];
end;

procedure TBase.SetItem(I: Integer; const V: Integer);
begin
  FItems[I] := V;
end;

function TBase.GetPart(Index: Integer): Integer;
begin
  Result := FItems[Index + 2];
end;

procedure TBase.SetPart(Index: Integer; const V: Integer);
begin
  FItems[Index + 2] := V;
end;

function TBase.GetName: string;
begin
  Result := FName;
end;

procedure TBase.SetName(const S: string);
begin
  FName := S;
end;

function TDerived.GetValue: Integer;
begin
  Result := inherited Value;
end;

procedure TDerived.SetValue2(V: Integer);
begin
  inherited Value := V + 1;
end;

function TDerived.GetPlain: Integer;
begin
  Result := inherited Plain * 2;
end;

procedure TDerived.SetPlain(V: Integer);
begin
  inherited Plain := V + 100;
end;

function TDerived.GetItem2(I: Integer): Integer;
begin
  Result := inherited Items[I] + 1000;
end;

procedure TDerived.SetItem2(I: Integer; const V: Integer);
begin
  inherited Items[I] := V * 2;
end;

function TDerived.GetSecond: Integer;
begin
  Result := -inherited Second;
end;

procedure TDerived.SetSecond(V: Integer);
begin
  inherited Second := V + inherited First;
end;

function TDerived.GetName: string;
begin
  Result := 'override';
end;

procedure TDerived.SetName(const S: string);
begin
  WriteLn('override called');
end;

function TDerived.GetName2: string;
begin
  Result := '<' + inherited Name + '>';
end;

procedure TDerived.SetName2(const S: string);
begin
  inherited Name := S + '!';
end;

class function TDerived.GetShared: Integer;
begin
  Result := inherited Shared + 1;
end;

class procedure TDerived.SetShared(V: Integer);
begin
  inherited Shared := V * 10;
end;

function TDerived.Sum: Integer;
begin
  Result := inherited Value + inherited Plain + inherited Items[0];
end;

var
  D: TDerived;
  B: TBase;
begin
  D := TDerived.Create;
  D.Value := 41;
  WriteLn(D.Value);
  D.Plain := 1;
  WriteLn(D.Plain);
  D.Items[0] := 5;
  D[1] := 6;
  WriteLn(D.Items[0], ' ', D[1]);
  D.First := 7;
  D.Second := 3;
  WriteLn(D.First, ' ', D.Second);
  D.Name := 'abc';
  WriteLn(D.Name);
  TDerived.Shared := 4;
  WriteLn(TDerived.Shared, ' ', TBase.Shared);
  WriteLn(D.Sum);
  B := D;
  WriteLn(B.Name, ' ', B[0], ' ', B.Second);
  D.Free;
end.
