// covers: RTL-957, RTL-967
library haltlib;

uses HaltUnit;

procedure Quit(Code: Integer); stdcall;
begin
  Writeln('library: Halt(', Code, ')');
  Halt(Code);
end;

procedure LibraryExit;
begin
  Writeln('library: exit procedure');
end;

exports
  Quit;

begin
  ExitProc := @LibraryExit;
end.
