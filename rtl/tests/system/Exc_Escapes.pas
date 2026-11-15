// covers: RTL-032, RTL-033, ABI-104, ABI-124
// opc-run: variants 1 2 3
program Exc_Escapes;
{ Exceptions that nothing handles (system.md section 3), without SysUtils, so ExceptProc is nil and the message is
  'Unhandled exception <ClassName> at $<16 hex digits>'. The variant is the last character of the command line.
  1: a raise in the main block reaches the top-level handler of _start: unhandled exception, exit procedures and
     finalizations still run, exit code 217.
  2: a raise in the initialization of ExcEscapeUnit: the same path; the main block never starts; the unit's
     initialization did not complete, so it is not finalised (ABI-104 step 2).
  3: a raise in the finalization of ExcEscapeUnit: reported like an unhandled exception, the remaining shutdown steps
     still run (the exit procedure ran before, as step 1; ExcEscapeFirst is finalised after the report), exit
     code 217. ExcEscapeFirst is initialised first and finalised last in every variant.
  The main block holds no managed temporary: with one, variant 2 ends in runtime error 216 instead, because the
  main block's cleanup pad releases the temporary before it is initialised (compiler defect reported with R-200). }
uses ExcEscapeFirst, ExcEscapeUnit;

var
  SavedExit: Pointer;
  V: Char;

procedure AtExit;
begin
  ExitProc := SavedExit;
  Writeln('exit procedure, ExitCode ', ExitCode);
end;

function Variant: Char;
var
  P: PChar;
begin
  P := CmdLine;
  while P^ <> #0 do
    Inc(P);
  Dec(P);
  Result := P^;
end;

begin
  SavedExit := ExitProc;
  ExitProc := @AtExit;
  Writeln('main');
  V := Variant;
  if V = '1' then
    raise EEscape.Create;
  Writeln('main done');
end.
