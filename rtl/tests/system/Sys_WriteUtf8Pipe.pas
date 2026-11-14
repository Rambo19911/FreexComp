// covers: RTL-091, RTL-092, RTL-093
program Sys_WriteUtf8Pipe;
{ Byte-stream output is UTF-8 without BOM (stdout is a pipe under the test runner). }
var
  A: AnsiChar;
begin
  Writeln('ä ö ü € 日本');                  { 2- and 3-byte sequences }
  Writeln(#$D83D#$DE00);                   { a pair in one argument: one 4-byte sequence }
  Write(#$D83D);
  Write(#$DE00);                           { a pair split across two Write statements: still one 4-byte sequence }
  Writeln;
  Writeln('lone high ', #$D83D);           { an unpaired high surrogate before the line end: EF BF BD }
  Writeln('lone low ', #$DE00);            { an unpaired low surrogate: EF BF BD }
  A := AnsiChar($E9);
  Writeln('AnsiChar widened ', A);         { byte E9 -> U+00E9 -> C3 A9 }
  Writeln(' ':8190, #$D83D#$DE00, '|');    { the 4-byte sequence crosses the 8192-byte buffer boundary }
  Writeln(ErrOutput, 'stderr ä');
end.
