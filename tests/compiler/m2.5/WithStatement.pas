// covers: LANG-375, LANG-310
program WithStatement;

type
  TInner = record
    A, B: Integer;
  end;
  TOuter = record
    Inner: TInner;
    Name: string;
    A: Integer;
  end;
  TCounter = class
    Count: Integer;
    Name: string;
    procedure Add(N: Integer);
    function Twice: Integer;
    property Value: Integer read Count write Count;
  end;

procedure TCounter.Add(N: Integer);
begin
  Count := Count + N;
end;

function TCounter.Twice: Integer;
begin
  Result := 2 * Count;
end;

var
  Calls: Integer;
  Shared: TCounter;

function Next: TCounter;
begin
  Inc(Calls);
  Result := Shared;
end;

function MakeInner: TInner;
begin
  Result.A := 4;
  Result.B := 5;
end;

procedure Locals(C: TCounter);
var
  Count: Integer;
begin
  Count := -1;
  with C do
    Count := 42;
  Writeln(Count, ' ', C.Count);
end;

var
  O: TOuter;
  Arr: array[1..2] of TInner;
  A: Integer;
  I: Integer;
begin
  A := 100;
  with O do
  begin
    A := 1;
    Name := 'outer';
    with Inner do
    begin
      A := 2;
      B := A + 1;
    end;
  end;
  Writeln(A, ' ', O.A, ' ', O.Inner.A, ' ', O.Inner.B, ' ', O.Name);
  with O, Inner do
    Writeln(A, ' ', B, ' ', Name);
  for I := 1 to 2 do
    with Arr[I] do
    begin
      A := I * 10;
      B := -I;
    end;
  Writeln(Arr[1].A, ' ', Arr[2].A, ' ', Arr[2].B);
  with MakeInner do
    Writeln(A + B);
  Shared := TCounter.Create;
  with Shared do
  begin
    Add(5);
    Value := Value + 1;
    Name := 'c';
    Writeln(Count, ' ', Twice, ' ', Name);
  end;
  Calls := 0;
  with Next do
  begin
    Add(1);
    Add(1);
  end;
  Writeln(Calls, ' ', Shared.Count);
  Locals(Shared);
  Shared.Free;
end.
