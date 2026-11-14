// covers: LANG-875, LANG-852
program IncludeFile;
{$I IncludeFileDecl.inc}
begin
  Writeln(Greeting, ' ', Twice(Base));
{$INCLUDE IncludeFileBody}
{$IFOPT Q+}
  Writeln('switch set in the include stays set');
{$ENDIF}
end.
