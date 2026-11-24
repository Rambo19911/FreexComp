program PlatConsole;

{ Platform check fixture: prints the version of the system it runs on. }

uses
  SysUtils;

begin
  Writeln(TOSVersion.Major, '.', TOSVersion.Minor, '.', TOSVersion.Build);
  Writeln(TOSVersion.ToString);
end.
