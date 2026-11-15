{ Shared unit of sysutils/SU_HookLifetime. Uses only System and is named before SysUtils in the program's uses
  clause, so it is initialised before SysUtils and finalised after it: both sections print the System hooks. }
unit SUHookProbe;

interface

procedure PrintHooks(const When: string);

implementation

procedure PrintHooks(const When: string);
begin
  Writeln(When, ': ErrorProc ', Assigned(ErrorProc), ', ExceptProc ', Assigned(ExceptProc), ', ExceptObjProc ',
    Assigned(ExceptObjProc), ', AssertErrorProc ', Assigned(AssertErrorProc), ', AbstractErrorProc ',
    Assigned(AbstractErrorProc));
end;

initialization
  PrintHooks('before SysUtils initialization');

finalization
  PrintHooks('after SysUtils finalization');
end.
