unit Log;

interface

var
  Count: Integer;

procedure Note(const S: string);

implementation

procedure Note(const S: string);
begin
  Inc(Count);
  Writeln(Count, ': ', S);
end;

begin
  Note('Log begin-end initialization');
end.