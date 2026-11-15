// covers: LANG-725, CMP-380
program AnonNested;

type
  TIntFunc = reference to function(X: Integer): Integer;
  TMaker = reference to function(N: Integer): TIntFunc;

function Compose: TMaker;
var
  Base: Integer;
begin
  Base := 1000;
  Result := function(N: Integer): TIntFunc
    var
      Local: Integer;
    begin
      Local := N * 2;
      Result := function(X: Integer): Integer
        begin
          Result := Base + Local + X;
          Local := Local + 1;
        end;
    end;
  Base := 2000;
end;

var
  M: TMaker;
  A, B: TIntFunc;
begin
  M := Compose;
  A := M(1);
  B := M(10);
  Writeln(A(5), ' ', A(5), ' ', B(0));
  M := nil;
  Writeln(A(0), ' ', B(0));
end.