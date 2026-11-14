// covers: LANG-250
program SetSizes;
type
  T8 = set of 0..7;
  T5 = set of 3..7;
  T9 = set of 0..8;
  T16 = set of 0..15;
  T17 = set of 0..16;
  T32 = set of 0..31;
  T33 = set of 0..32;
  T64 = set of 0..63;
  T65 = set of 0..64;
  T256 = set of Byte;
  TColor = (Red, Green, Blue);
  TColors = set of TColor;
  TChars = set of AnsiChar;
  TLetters = set of 'a'..'z';
  TFlags = set of Boolean;
var
  S16: T16;
  S64: T64;
begin
  Writeln(SizeOf(T8), ' ', SizeOf(T5), ' ', SizeOf(T9), ' ', SizeOf(T16), ' ', SizeOf(T17));
  Writeln(SizeOf(T32), ' ', SizeOf(T33), ' ', SizeOf(T64), ' ', SizeOf(T65), ' ', SizeOf(T256));
  Writeln(SizeOf(TColors), ' ', SizeOf(TChars), ' ', SizeOf(TLetters), ' ', SizeOf(TFlags));
  Writeln(SizeOf(S16), ' ', SizeOf(S64));
end.
