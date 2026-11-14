// covers: LANG-214, LANG-301, LANG-302, LANG-921
program BooleanOps;
var
  T, F, B: Boolean;
  I: Integer;
begin
  T := True;
  F := False;
  Writeln(T and F, ' ', T or F, ' ', T xor F, ' ', not T, ' ', not F);
  Writeln(T and T, ' ', F or F, ' ', T xor T, ' ', F xor F);
  Writeln(T = F, ' ', T <> F, ' ', F < T, ' ', T >= F, ' ', T <= F, ' ', F > T);
  I := 5;
  B := (I > 3) and (I < 10);
  Writeln(B);
  B := (I < 3) or (I = 6);
  Writeln(B);
  Writeln(I = 5, ' ', I <> 5, ' ', I <= 4, ' ', I >= 5, ' ', I < 6, ' ', I > 6);
  Writeln(Ord(T), ' ', Ord(F), ' ', Ord(I > 0));
  Writeln(Succ(False), ' ', Pred(True), ' ', Low(Boolean), ' ', High(Boolean));
  Writeln(not (I > 3) or T and F);
  Writeln(True and not False);
end.
