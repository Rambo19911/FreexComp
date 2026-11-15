// covers: LANG-485, LANG-720, LANG-721, CMP-380
program AnonFromRoutineAndMethod;

type
  TIntFunc = reference to function(X: Integer): Integer;
  TProc = reference to procedure;

  TCounter = class
    Count: Integer;
    procedure Bump;
    function Scale(X: Integer): Integer;
    function MakeReader: TIntFunc;
  end;

function Twice(X: Integer): Integer;
begin
  Result := X * 2;
end;

procedure Hello;
begin
  Writeln('hello');
end;

procedure TCounter.Bump;
begin
  Inc(Count);
end;

function TCounter.Scale(X: Integer): Integer;
begin
  Result := X * Count;
end;

function TCounter.MakeReader: TIntFunc;
begin
  Result := function(X: Integer): Integer begin Result := X + Count + Self.Count end;
end;

function Call(const F: TIntFunc; X: Integer): Integer;
begin
  Result := F(X);
end;

var
  F: TIntFunc;
  P: TProc;
  C: TCounter;
begin
  F := Twice;
  Writeln(F(21));
  Writeln(Call(Twice, 5));
  P := Hello;
  P();
  C := TCounter.Create;
  C.Count := 3;
  F := C.Scale;
  Writeln(F(5));
  P := C.Bump;
  P();
  P;
  Writeln(F(5));
  Writeln(C.Count);
  F := C.MakeReader;
  C.Count := 20;
  Writeln(F(2));
  Writeln(Call(C.Scale, 2));
  F := nil;
  P := nil;
  C.Free;
end.