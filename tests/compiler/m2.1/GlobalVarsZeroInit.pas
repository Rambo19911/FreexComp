// covers: LANG-391, LANG-215
program GlobalVarsZeroInit;
var
  G1: Integer;
  G2: Int64;
  G3: Boolean;
  G4: Char;
  G5: Byte;
  G6: UInt64;
  G7: LongBool;
  Init1: Integer = 42;
  Init2: Boolean = True;
  Init3: Char = 'q';
  Init4: Int64 = -5000000000;
  Init5: Cardinal = $FFFFFFFF;
  Init6: ShortInt = -128;
  Init7: Word = 1000 * 60;

procedure Bump;
begin
  Init1 := Init1 + 1;
  G1 := G1 - 1;
end;

begin
  Writeln(G1, ' ', G2, ' ', G3, ' ', Ord(G4), ' ', G5, ' ', G6, ' ', G7);
  Writeln(Init1, ' ', Init2, ' ', Init3, ' ', Init4, ' ', Init5, ' ', Init6, ' ', Init7);
  Bump;
  Bump;
  Writeln(Init1, ' ', G1);
end.
