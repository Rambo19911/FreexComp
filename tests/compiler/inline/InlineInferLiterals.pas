// covers: LANG-573
program InlineInferLiterals;

{ var X := e takes the natural type of a literal: an integer is Integer, Int64 or UInt64 by its value, a real Double,
  a single character Char, a longer string string, True Boolean. A cast keeps its type: Byte(5) declares a Byte, which
  goes to Show(Integer) by widening (there is no Show(Byte)). Each overload prints its parameter type. }

procedure Show(I: Integer); overload;
begin
  Writeln('Integer ', I);
end;

procedure Show(I: Int64); overload;
begin
  Writeln('Int64 ', I);
end;

procedure Show(I: UInt64); overload;
begin
  Writeln('UInt64 ', I);
end;

procedure Show(D: Double); overload;
begin
  Writeln('Double ', D:0:2);
end;

procedure Show(C: Char); overload;
begin
  Writeln('Char ', C);
end;

procedure Show(const S: string); overload;
begin
  Writeln('string ', S);
end;

procedure Show(B: Boolean); overload;
begin
  Writeln('Boolean ', B);
end;

procedure Run;
begin
  var Small := 5;
  Show(Small);
  Writeln(SizeOf(Small));
  var Large := 3000000000;
  Show(Large);
  Writeln(SizeOf(Large));
  var Huge := 18446744073709551615;
  Show(Huge);
  Writeln(SizeOf(Huge));
  var Real := 2.5;
  Show(Real);
  Writeln(SizeOf(Real));
  var Letter := 'x';
  Show(Letter);
  Writeln(SizeOf(Letter));
  var Text := 'text';
  Show(Text);
  Writeln(SizeOf(Text) = SizeOf(Pointer));
  var Flag := True;
  Show(Flag);
  Writeln(SizeOf(Flag));
  var Tiny := Byte(5);
  Show(Tiny);
  Writeln(SizeOf(Tiny));
end;

begin
  Run;
end.
