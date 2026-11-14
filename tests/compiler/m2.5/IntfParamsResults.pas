// covers: LANG-662, CMP-325
program IntfParamsResults;

{ Interfaces as value, const, var and out parameters, function results, class fields (InitTable), record fields,
  dynamic and static array elements: each object is freed exactly when its last reference goes. The result of each Make
  call in the main block lives in its own temporary until the block ends (CMP-325 rule 5), so objects 1, 6, 7 and 8 are
  freed there, in call order; S[1] (object 9) goes with the program's globals. }

type
  IValue = interface
    function Get: Integer;
  end;

  TValue = class(TInterfacedObject, IValue)
    FV: Integer;
    constructor Create(V: Integer);
    destructor Destroy; override;
    function Get: Integer;
  end;

  THolder = class
    Item: IValue;
  end;

  TRec = record
    Item: IValue;
    N: Integer;
  end;

constructor TValue.Create(V: Integer);
begin
  FV := V;
end;

destructor TValue.Destroy;
begin
  Writeln('free ', FV);
  inherited Destroy;
end;

function TValue.Get: Integer;
begin
  Result := FV;
end;

function Make(V: Integer): IValue;
begin
  Result := TValue.Create(V);
end;

procedure ShowValue(I: IValue);
begin
  Writeln('value ', I.Get);
end;

procedure ShowConst(const I: IValue);
begin
  Writeln('const ', I.Get);
end;

procedure Replace(var I: IValue; V: Integer);
begin
  I := Make(V);
end;

procedure Produce(out I: IValue);
begin
  I := Make(5);
end;

var
  A: IValue;
  H: THolder;
  R: TRec;
  Arr: array of IValue;
  S: array[0..1] of IValue;
begin
  A := Make(1);
  ShowValue(A);
  ShowConst(A);
  Replace(A, 3);
  Writeln(A.Get);
  Produce(A);
  Writeln(A.Get);
  H := THolder.Create;
  H.Item := A;
  A := nil;
  Writeln(H.Item.Get);
  H.Free;
  R.Item := Make(6);
  R.N := 1;
  R.Item := nil;
  SetLength(Arr, 2);
  Arr[0] := Make(7);
  Arr[1] := Arr[0];
  Arr := nil;
  S[0] := Make(8);
  S[1] := Make(9);
  S[0] := S[1];
  Writeln('end');
end.
