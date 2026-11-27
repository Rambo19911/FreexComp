unit IndentRoutines;
interface
function Outer(A: Integer): Integer;

implementation
function Outer(A: Integer): Integer;
var
  B: Integer;
  function Inner(C: Integer): Integer;
    function Innermost: Integer;
    begin
      Result := 1;
    end;
  begin
    Result := C + Innermost;
  end;
begin
  B := Inner(A);
  Result := B;
end;

initialization
  Outer(1);

finalization
  Outer(2);
end.
