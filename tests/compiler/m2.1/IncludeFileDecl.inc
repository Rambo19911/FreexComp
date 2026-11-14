{ declarations included by IncludeFile.pas }
const
  Greeting = 'included';
  Base = 21;

function Twice(N: Integer): Integer;
begin
  Result := N * 2;
end;
