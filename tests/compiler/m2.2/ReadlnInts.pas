// covers: LANG-940, LANG-941, LANG-942, CMP-411
program ReadlnInts;
// Integers from Input: whitespace (space, tab, CR, LF) is skipped, signs and $hex are read, Read stops after the number
// and Readln drops the rest of the line (CR LF, lone LF and lone CR all end a line); a value in the variable's range is
// stored (Byte, Int64 extremes, Cardinal); at end of file before any digit the variable becomes 0 without an error.
var
  A, B, C, N, I: Integer;
  Total: Int64;
  W: Byte;
  H: Int64;
  U: Cardinal;
begin
  Readln(A, B, C);
  Writeln(A + B + C);
  Readln(N);
  Total := 0;
  for I := 1 to N do
  begin
    Read(A);
    Total := Total + A;
  end;
  Readln;
  Writeln(Total);
  Readln(W, H);
  Writeln(W, ' ', H);
  Readln(H);
  Writeln(H);
  Read(U);
  Writeln(U);
  A := 99;
  Readln(A);
  Writeln(A);
end.
