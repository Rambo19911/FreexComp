unit Calc;

interface

function Add(A, B: Integer): Integer;
procedure Show(const S: string);
function Twice(N: Integer): Integer;

implementation

procedure Prefix(N: Integer); forward;

function Add;
begin
  Result := A + B;
end;

procedure Show;
begin
  Prefix(Length(S));
  Writeln(S, ' ', Twice(Length(S)));
end;

procedure Prefix(N: Integer);
begin
  Write(N, ': ');
end;

function Twice(N: Integer): Integer;
begin
  Result := Add(N, N);
end;

end.
