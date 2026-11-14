// covers: LANG-830, ABI-121, RTL-213
program ExceptionAccessViolation;

{ a nil dereference in a callee is caught as EAccessViolation (SysUtils' ExceptObjProc) }

uses
  SysUtils, Faults;

{ the string temporaries live until the end of this routine, before the leak counter is read }
procedure Report(E: EAccessViolation);
var
  M: string;
begin
  M := E.Message;
  Writeln(E.ClassName, ' (', Copy(M, 1, 16), '...', Copy(M, Length(M) - 33, 34), ')');
  Writeln('is EExternal: ', E is EExternal, ', code $C0000005: ', E.ExceptionCode = $C0000005);
end;

var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  try
    Poke(nil);
    Writeln('not reached');
  except
    on E: EAccessViolation do Report(E);
  end;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.