program OverloadFirstByCount;
function {#}Sum(A, B: Integer): Integer; overload;
begin
  Result := A + B;
end;
function Sum(A, B, C: Integer): Integer; overload;
begin
  Result := A + B + C;
end;
begin
  Writeln({|}Sum(1, 2));
end.
