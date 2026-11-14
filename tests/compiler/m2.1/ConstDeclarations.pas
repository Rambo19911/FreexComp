// covers: LANG-201, LANG-340, LANG-343
program ConstDeclarations;
const
  Ten = 10;
  Twenty = Ten * 2;
  Mask = $FF;
  Neg = -Ten;
  Half = MaxInt div 2 + 1;
  Hello = 'Hello';
  Letter = 'L';
  Yes = True;
  NotYes = not Yes;
  Combined = (Ten + Twenty) mod 7;
  Shifted = 1 shl 10;
  OrdA = Ord('A');
  Ch = Chr(OrdA + 2);
  Hi8 = High(Byte);
  SizeInt = SizeOf(Integer);
  NextCh = Succ(Ch);
  Both = Yes and not NotYes;
  Folded = Abs(Neg) + Sqr(3) + Ord(Odd(7)) + Lo($1234) + Hi($1234) + Swap($0102);
  Cmp = Ten > Twenty;
  Last = Pred(Low(Integer) + 1);

procedure ShowLocal;
const
  Ten = 'ten';
  Local = Twenty + 1;
begin
  Writeln(Ten, ' ', Local);
end;

var
  X: Integer;
begin
  Writeln(Ten, ' ', Twenty, ' ', Mask, ' ', Neg, ' ', Half);
  Writeln(Hello, ' ', Letter, ' ', Yes, ' ', NotYes, ' ', Combined);
  Writeln(Shifted, ' ', OrdA, ' ', Ch, ' ', Hi8, ' ', SizeInt, ' ', NextCh);
  Writeln(Both, ' ', Folded, ' ', Cmp, ' ', Last, ' ', MaxInt);
  ShowLocal;
  X := Ten;
  X := X * Twenty + Mask;
  Writeln(X);
end.
