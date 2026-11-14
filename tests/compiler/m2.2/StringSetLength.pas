// covers: CMP-412, LANG-241
program StringSetLength;
var
  S, T: string;
  I: Integer;
begin
  S := 'abcdef';
  SetLength(S, 3);
  Writeln(S, ' ', Length(S));
  T := S;
  SetLength(S, 5);
  S[4] := 'X';
  S[5] := 'Y';
  Writeln(S, ' ', Length(S));
  Writeln(T, ' ', Length(T));
  SetLength(S, 0);
  Writeln('[', S, '] ', Length(S));
  SetLength(S, -4);
  Writeln('[', S, '] ', Length(S));
  SetLength(S, 26);
  for I := 1 to 26 do
    S[I] := Chr(Ord('a') + I - 1);
  Writeln(S, ' ', Length(S));
  T := S;
  SetLength(T, 26);
  T[1] := 'Z';
  Writeln(S[1], T[1]);
  S := 'lit';
  SetLength(S, 2);
  Writeln(S);
end.
