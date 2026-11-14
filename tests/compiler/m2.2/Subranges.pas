// covers: LANG-211, LANG-212, LANG-213, LANG-280, LANG-351
program Subranges;
type
  TPercent = 0..100;
  TSigned = -1..100;
  TByteRange = 0..200;
  TSmall = -1..200;
  TWordRange = 0..60000;
  TIntRange = -40000..40000;
  TCardRange = 0..3000000000;
  TBigRange = -1..3000000000;
  TLetter = 'a'..'z';
  TColor = (Red, Green, Blue, Black);
  TBright = Green..Blue;
var
  P: TPercent;
  L: TLetter;
  B: TBright;
  I: Integer;
  C: TColor;
begin
  Writeln(SizeOf(TPercent), ' ', SizeOf(TSigned), ' ', SizeOf(TByteRange), ' ', SizeOf(TSmall));
  Writeln(SizeOf(TWordRange), ' ', SizeOf(TIntRange), ' ', SizeOf(TCardRange), ' ', SizeOf(TBigRange));
  Writeln(SizeOf(TLetter), ' ', SizeOf(TBright));
  Writeln(Low(TPercent), ' ', High(TPercent), ' ', Low(TSigned), ' ', High(TCardRange));
  Writeln(Low(TLetter), High(TLetter), ' ', Ord(Low(TBright)), ' ', Ord(High(TBright)));
  P := 50;
  I := P + 60;
  Writeln(I);
  P := P * 2;
  Writeln(P);
  L := 'q';
  Writeln(L, Succ(L));
  C := Blue;
  B := C;
  Writeln(Ord(B));
  C := B;
  Writeln(Ord(C));
{$R-}
  I := 150;
  P := I;
  Writeln(P);
{$R+}
  I := 100;
  P := I;
  Writeln(P);
  I := 101;
  P := I;
  Writeln('never');
end.
