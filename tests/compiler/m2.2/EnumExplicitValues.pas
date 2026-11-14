// covers: LANG-210
program EnumExplicitValues;
type
  TCode = (cA = 1, cB = 5, cC, cD = 10);
  TBig = (bLow, bHigh = 300);
  TWide = (wOne, wTwo = 70000);
{$Z4}
  TPadded = (pOne, pTwo);
{$Z1}
var
  X: TCode;
  B: TBig;
  W: TWide;
begin
  Writeln(Ord(cA), ' ', Ord(cB), ' ', Ord(cC), ' ', Ord(cD));
  Writeln(Ord(Low(TCode)), ' ', Ord(High(TCode)));
  X := cB;
  X := Succ(X);
  Writeln(Ord(X));
  X := Succ(X);
  Writeln(Ord(X));
  X := TCode(8);
  Writeln(Ord(X), ' ', X > cC, ' ', X < cD);
  Writeln(SizeOf(TCode), ' ', SizeOf(TBig), ' ', SizeOf(TWide), ' ', SizeOf(TPadded));
  B := bHigh;
  Writeln(Ord(B), ' ', Ord(Pred(bHigh)));
  W := wTwo;
  Dec(W);
  Writeln(Ord(W));
end.
