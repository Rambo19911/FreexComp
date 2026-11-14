// covers: LANG-290, CMP-313
program Typecasts;
type
  TColor = (Red, Green, Blue);
  TRec = record Lo, Hi: Integer; end;
  TBytes = array[0..3] of Byte;
  TSmall = set of 0..7;
  PInteger = ^Integer;
var
  I: Integer;
  B: Byte;
  S: ShortInt;
  C: Cardinal;
  Q: Int64;
  E: TColor;
  F: Boolean;
  P: Pointer;
  PI: PInteger;
  N: NativeInt;
  R: TRec;
  Bytes: TBytes;
  Bits: TSmall;
  D: Double;

procedure Twice(var X: Integer);
begin
  X := X * 2;
end;

begin
  { ordinal casts: truncation and extension by the source's signedness }
  I := -1;
  Writeln(Byte(I), ' ', Word(I), ' ', Cardinal(I));
  B := 200;
  Writeln(ShortInt(B), ' ', Integer(B));
  S := -2;
  Writeln(Word(S), ' ', Int64(S));
  E := TColor(I and 2);
  Writeln(Ord(E), ' ', Ord(TColor(1)), ' ', Char(66), ' ', Integer('A'));
  F := True;
  Writeln(Boolean(B), ' ', Integer(F), ' ', Boolean(0));

  { pointer casts }
  I := 42;
  P := @I;
  PI := PInteger(P);
  Writeln(PI^);
  N := NativeInt(P);
  PI := PInteger(N);
  PI^ := 7;
  Writeln(I, ' ', NativeInt(Pointer(I)));
  S := -1;
  B := 255;
  Writeln(NativeInt(Pointer(S)), ' ', NativeInt(Pointer(B)));

  { variable typecasts }
  Q := $0000000500000003;
  R := TRec(Q);
  Writeln(R.Lo, ' ', R.Hi);
  TRec(Q).Hi := 9;
  Writeln(Q);
  Int64(R) := -1;
  Writeln(R.Lo, ' ', R.Hi);
  C := $04030201;
  Bytes := TBytes(C);
  Writeln(Bytes[0], ' ', Bytes[3]);
  B := 5;
  Bits := TSmall(B);
  Writeln(0 in Bits, ' ', 1 in Bits, ' ', 2 in Bits);
  Byte(Bits) := 2;
  Writeln(1 in Bits);
  Integer(C) := -1;
  Writeln(C);
  Inc(Integer(C), 3);
  Twice(Integer(C));
  Writeln(C);

  { integer -> real }
  D := Double(I);
  Writeln(D = 7);
end.
