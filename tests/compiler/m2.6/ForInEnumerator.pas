// covers: LANG-362
program ForInEnumerator;

type
  TRangeEnum = record
    FCur, FLast: Integer;
    function MoveNext: Boolean;
    function GetCurrent: Integer;
    property Current: Integer read GetCurrent;
  end;

  TRange = record
    Low, High: Integer;
    function GetEnumerator: TRangeEnum;
  end;

  TBagEnum = class;

  TBag = class
    Items: array[0..2] of string;
    function GetEnumerator: TBagEnum;
  end;

  TBagEnum = class
  private
    FBag: TBag;
    FIndex: Integer;
  public
    constructor Create(ABag: TBag);
    destructor Destroy; override;
    function MoveNext: Boolean;
    function GetCurrent: string;
    property Current: string read GetCurrent;
  end;

function TRangeEnum.MoveNext: Boolean;
begin
  Inc(FCur);
  Result := FCur <= FLast;
end;

function TRangeEnum.GetCurrent: Integer;
begin
  Result := FCur;
end;

function TRange.GetEnumerator: TRangeEnum;
begin
  Result.FCur := Low - 1;
  Result.FLast := High;
end;

function TBag.GetEnumerator: TBagEnum;
begin
  Result := TBagEnum.Create(Self);
end;

constructor TBagEnum.Create(ABag: TBag);
begin
  inherited Create;
  FBag := ABag;
  FIndex := -1;
end;

destructor TBagEnum.Destroy;
begin
  Writeln('freed');
  inherited Destroy;
end;

function TBagEnum.MoveNext: Boolean;
begin
  Inc(FIndex);
  Result := FIndex <= 2;
end;

function TBagEnum.GetCurrent: string;
begin
  Result := FBag.Items[FIndex];
end;

var
  R: TRange;
  B: TBag;
  S: string;
  N: Integer;
begin
  R.Low := 3;
  R.High := 6;
  for N in R do
    Write(' ', N);
  Writeln;
  B := TBag.Create;
  B.Items[0] := 'red';
  B.Items[1] := 'green';
  B.Items[2] := 'blue';
  for S in B do
    Writeln(S);
  for var W in B do
    if W = 'green' then
      Break;
  Writeln('done');
  B.Free;
end.