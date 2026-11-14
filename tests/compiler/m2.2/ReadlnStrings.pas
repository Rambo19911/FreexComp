// covers: LANG-940, LANG-941, LANG-945, LANG-946, ABI-131, CMP-411
program ReadlnStrings;
// Lines from Input: a string reads up to (not including) the line terminator, so a second Read without Readln gives ''
// again; empty lines; UTF-8 input decoded (a surrogate pair counts two); Eof ends the loop, the last line has no
// terminator, and a string read at end of file is ''.
var
  S, T: string;
  Count: Integer;
begin
  Readln(S);
  Writeln('[', S, '] ', Length(S));
  Readln(S);
  Writeln('[', S, '] ', Length(S));
  Read(S);
  Read(T);
  Writeln('[', S, '][', T, ']');
  Readln;
  Readln(S);
  Writeln('[', S, '] ', Length(S));
  Count := 0;
  while not Eof do
  begin
    Readln(S);
    Count := Count + 1;
    Writeln(Count, ': [', S, ']');
  end;
  S := 'old';
  Readln(S);
  Writeln('[', S, '] ', Eof);
end.
