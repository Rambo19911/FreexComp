unit Types;

interface

var
  InitCount: Integer;

function Twice(I: Integer): Integer;

implementation

function Twice(I: Integer): Integer;
begin
  Result := 2 * I;
end;

initialization
  Inc(InitCount);
  Writeln('init Types');
end.
