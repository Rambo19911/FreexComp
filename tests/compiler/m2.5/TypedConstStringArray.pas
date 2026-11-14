// covers: LANG-344, ABI-020
program TypedConstStringArray;

{ Typed constant arrays of managed types: month names (array[1..12] of string), a two-dimensional table of strings,
  an AnsiString array, a record with a string field inside an array, and a routine-local one. The literals are
  constant (reference count -1, ABI-020): reading and copying them never frees them. }

type
  TEntry = record
    Key: Integer;
    Name: string;
  end;

const
  Months: array[1..12] of string = ('January', 'February', 'March', 'April', 'May', 'June', 'July', 'August',
    'September', 'October', 'November', 'December');
  Grid: array[0..1, 0..2] of string = (('a', 'b', 'c'), ('d', '', 'f'));
  Bytes: array[0..1] of AnsiString = ('ansi', 'text');
  Entries: array[0..1] of TEntry = ((Key: 1; Name: 'one'), (Key: 2; Name: 'two'));

function DayName(D: Integer): string;
const
  Days: array[0..6] of string = ('Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat');
begin
  Result := Days[D mod 7];
end;

var
  I: Integer;
  S: string;
begin
  for I := 1 to 12 do
    if Length(Months[I]) > 6 then
      Writeln(I, ' ', Months[I]);
  S := Months[2];
  S := S + '!';
  Writeln(S, ' ', Months[2]);
  Writeln(Grid[0, 2], Grid[1, 0], '[', Grid[1, 1], ']', Length(Grid[1, 1]));
  Writeln(Bytes[0], ' ', Bytes[1]);
  Writeln(Entries[1].Key, ' ', Entries[1].Name, ' ', Entries[0].Name);
  Writeln(DayName(1), ' ', DayName(13));
end.
