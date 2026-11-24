// covers: LANG-245, LANG-280
program ErrCharArrayToPChar;

{ A zero-based character array is not assignment-compatible with PChar (C-271): LANG-245 lists the array only as a source
  of a string, and LANG-280 makes everything unlisted E2010 - as an argument and in an assignment. The address is taken
  explicitly instead (@Buf is an untyped pointer, LANG-265). }

procedure Show(P: PChar);
begin
  WriteLn(P);
end;

var
  Buf: array[0..7] of Char;
  P: PChar;
begin
  Buf[0] := 'a';
  Buf[1] := #0;
  Show(Buf);
  P := Buf;
  P := @Buf;
  Show(@Buf[0]);
end.
