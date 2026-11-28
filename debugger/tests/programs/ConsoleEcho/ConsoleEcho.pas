program ConsoleEcho;
uses SysUtils;
var
  Line: string;
  I: Integer;
begin
  WriteLn('args ', ParamCount);
  for I := 1 to ParamCount do
    WriteLn('[', ParamStr(I), ']');
  WriteLn('env ', GetEnvironmentVariable('FREEXCOMP_CONSOLE_TEST'));
  WriteLn('dir ', GetCurrentDir);
  WriteLn('ready');
  ReadLn(Line);
  WriteLn('got: ', Line);
  ReadLn(Line);
  if Line = 'fail' then
    Halt(3);
  WriteLn('bye');
end.
