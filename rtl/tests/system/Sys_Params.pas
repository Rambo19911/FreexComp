// covers: RTL-034
// opc-run: rawargs one   "two words" back\slash a\\\"b c\\"d e" f"g"h "x""y" "" äöü trailing\\
program Sys_Params;
{ The runner passes the raw command line above after the quoted program path. Expected arguments (RTL-034):
  'one' (runs of separators collapse); 'two words' (quoted separators); 'back\slash' (a backslash not before a quote
  is literal); 'a\"b' (2n+1 backslashes and a quote: n backslashes and a literal quote); 'c\d e' (2n backslashes and
  a quote: n backslashes, the quote toggles quoting); 'fgh' (quotes inside an argument only toggle); 'x"y' ("" inside
  quotes is one quote); '' (an empty quoted argument); 'äöü'; 'trailing\\'. ParamStr(0) is the module path, not
  the command-line text; indexes outside 0..ParamCount give ''. }
var
  I: Integer;
  S: string;
begin
  Writeln('count ', ParamCount);
  for I := 1 to ParamCount do
    Writeln(I, ' [', ParamStr(I), ']');
  S := ParamStr(0);
  Writeln('module ', (Length(S) > 15) and (S[2] = ':') and (S[3] = '\'),
    ' ', Pos('\Sys_Params.exe', S) = Length(S) - 14);
  Writeln('outside [', ParamStr(ParamCount + 1), '] [', ParamStr(-1), ']');
end.
