// covers: LANG-940, LANG-943, LANG-944, LANG-946, CMP-411
program ReadMixed;
// Read of integers, reals (Double and Single, exponents, INF/-INF/NaN in any case) and chars (no whitespace skipping; a
// lone LF is #10; #26 at end of file), with and without the file argument; Eoln and Eof with and without (Input); at end
// of file numbers read as 0.
var
  I: Integer;
  X, Y: Double;
  F: Single;
  C, D: Char;
begin
  Read(I, X);
  Writeln(I, ' ', X:0:3);
  Read(C);
  Read(D);
  Writeln(Ord(C), ' ', D);
  Read(C);
  Writeln(C, ' ', Eoln);
  Read(C);
  Writeln(Ord(C), ' ', Eoln);
  Read(Y, F);
  Writeln(Y:0:2, ' ', F:0:4);
  Read(Input, X, Y);
  Writeln(X:0, ' ', Y:0);
  Read(X);
  Writeln(X:0, ' ', Eoln(Input));
  Readln;
  Read(I);
  Writeln(I, ' ', Eof, ' ', Eoln);
  Readln;
  Writeln(Eof(Input), ' ', Eoln);
  Read(C, I, X);
  Writeln(Ord(C), ' ', I, ' ', X:0:1);
end.
