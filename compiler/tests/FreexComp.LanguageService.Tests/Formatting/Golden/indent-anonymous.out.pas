program IndentAnonymous;
type
  TProc = reference to procedure;
  TFunc = reference to function(X: Integer): Integer;
procedure Run(P: TProc);
begin
  P;
end;
var
  F: TFunc;
begin
  Run(procedure
    begin
      Writeln('in');
    end);
  F := function(X: Integer): Integer
    begin
      Result := X * 2;
    end;
end.
