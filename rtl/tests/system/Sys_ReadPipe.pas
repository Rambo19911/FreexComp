// covers: RTL-091, RTL-092, RTL-110, RTL-111, RTL-112, RTL-113, LANG-941, LANG-942, LANG-943, LANG-944, LANG-945, LANG-946
program Sys_ReadPipe;
{ Read/Readln from a byte-stream Input (stdin is a pipe under the test runner). The .in file (exact bytes) starts with a
  UTF-8 BOM, mixes CR LF, lone LF and lone CR line ends, has blank lines, and two long lines of 3-byte and 4-byte UTF-8
  sequences (9000 and 10000 bytes) so that sequences are split at every read-block boundary however the pipe delivers
  its bytes. }
var
  I, J, K: Integer;
  X: Double;
  C: Char;
  S: string;
  Ok: Boolean;
begin
  Read(I, J);                              { BOM skipped; blanks before numbers skipped }
  Writeln(I, ' ', J, ' ', Eoln, ' ', Eof);
  Readln;
  Read(I, J, X);                           { $hex, explicit plus sign, exponent }
  Writeln(I, ' ', J, ' ', X:0:1);
  Readln;
  Readln(S);                               { a lone CR ends the line }
  Writeln('[', S, '] ', Length(S));
  Read(C);
  Writeln(C, ' ', Eoln);
  Read(C);
  Read(C);
  Writeln(C, ' ', Eoln);
  Readln;
  Writeln(SeekEoln, ' ', Eoln);            { spaces and a tab, then the line end }
  Readln;
  Readln(S);
  Ok := Length(S) = 3000;
  for K := 1 to Length(S) do
    if S[K] <> #$20AC then
      Ok := False;
  Writeln('euro ', Length(S), ' ', Ok);
  Readln(S);
  Ok := Length(S) = 5000;
  K := 1;
  while K < Length(S) do
  begin
    if (S[K] <> #$D83D) or (S[K + 1] <> #$DE00) then
      Ok := False;
    K := K + 2;
  end;
  Writeln('pairs ', Length(S), ' ', Ok);
  Writeln(SeekEof, ' ', Eof);              { blank lines, then 42 }
  Read(I);
  Writeln(I, ' ', SeekEoln);               { trailing blanks of the line are skipped }
  Writeln(SeekEof, ' ', Eof, ' ', Eoln);   { only blanks and line ends are left }
  Read(C, I, X);
  Readln(S);
  Writeln(Ord(C), ' ', I, ' ', X:0:1, ' [', S, ']');
end.
