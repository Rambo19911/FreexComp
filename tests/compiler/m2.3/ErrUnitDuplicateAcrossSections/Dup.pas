unit Dup;
interface
const
  Limit = 10;
function Twice(X: Integer): Integer;
procedure Show(X: Integer); overload;
implementation
var
  Limit: Integer;
procedure Show(X: Integer);
begin
end;
procedure Show(const S: string); overload;
begin
end;
function Twice(X: Integer): Integer;
begin
  Result := 2 * X;
end;
type
  Twice = Integer;
end.