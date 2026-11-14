// covers: LANG-385
program InlineVars;

procedure Show;
begin
  var A := 5;
  var B: Int64 := 3000000000;
  var C := 'x';
  var S := 'text';
  var D := 2.5;
  var Big := 3000000000;
  var Flag := A > 3;
  Writeln(A, ' ', B, ' ', C, ' ', S, ' ', D:0:1, ' ', Big, ' ', Flag);
  Writeln(SizeOf(A), ' ', SizeOf(C), ' ', SizeOf(D), ' ', SizeOf(Big));
  begin
    var Inner := A * 2;
    Writeln(Inner);
  end;
  var Inner := 'again';
  Writeln(Inner);
  var I: Integer;
  I := 3;
  while I > 0 do
  begin
    var Sq := I * I;
    Write(' ', Sq);
    Dec(I);
  end;
  Writeln;
  const K = 10;
  Writeln(K * A);
end;

begin
  Show;
  var M := 'main';
  Writeln(M);
end.