// covers: RTL-1067, RTL-1068, RTL-1069, RTL-1070
// phase: 8
// opc: -D DEBUG
// opc-run: variants 1 2 3 4 5
program Sys_MemoryManagerSwitchCheck;
{ The check of a late manager switch in a debug build (System compiled with DEBUG). Variant 1: a switch while a
  block is live is runtime error 204; the line after the call is not reached. Variant 2: a switch with no live
  block succeeds, a block allocated and released under the new manager leaves nothing live, so the switch back
  succeeds too. Variant 3: CheckMemoryManagerSwitch = False allows a switch with a live block. Variant 4: setting
  the current manager again is no switch. Variant 5: with the exception support of SysUtils the error arrives as
  EInvalidPointer and the manager is unchanged. The unit SysUtils is used for variant 5; the other variants clear
  ErrorProc, so they show the bare runtime. Its start-up blocks are live in every variant, which is why variant 2
  starts its count with a first, unchecked switch. The managers forward to the default one. The variant is
  ParamStr(1). }
uses
  SysUtils;

var
  Old, First, Second, Back: TMemoryManager;
  P: Pointer;
  V: string;

function FirstGetMem(Size: NativeInt): Pointer;
begin
  Result := Old.GetMem(Size);
end;

function SecondGetMem(Size: NativeInt): Pointer;
begin
  Result := Old.GetMem(Size);
end;

function Current(const M: TMemoryManager): Boolean;
begin
  GetMemoryManager(Back);
  Result := Back.GetMem = M.GetMem;
end;

begin
  V := ParamStr(1);
  GetMemoryManager(Old);
  First := Old;
  First.GetMem := FirstGetMem;
  Second := Old;
  Second.GetMem := SecondGetMem;
  if V <> '5' then
    ErrorProc := nil;
  Writeln('check is on by default ', CheckMemoryManagerSwitch);
  if V = '1' then
  begin
    GetMem(P, 16);
    Writeln('switching with a live block');
    SetMemoryManager(First);
    Writeln('not reached');
  end
  else if V = '2' then
  begin
    CheckMemoryManagerSwitch := False;
    SetMemoryManager(First);           { passes unchecked; the count of live blocks starts again at 0 }
    CheckMemoryManagerSwitch := True;
    SetMemoryManager(Second);
    Writeln('switch with no live block ', Current(Second));
    GetMem(P, 16);
    FreeMem(P);
    SetMemoryManager(First);
    Writeln('switch back after a block was allocated and released ', Current(First));
    CheckMemoryManagerSwitch := False;
    SetMemoryManager(Old);
  end
  else if V = '3' then
  begin
    GetMem(P, 16);
    CheckMemoryManagerSwitch := False;
    SetMemoryManager(First);
    Writeln('switch with a live block, check off ', Current(First));
    FreeMem(P);
    SetMemoryManager(Old);
    Writeln('restored ', not IsMemoryManagerSet);
  end
  else if V = '4' then
  begin
    GetMem(P, 16);
    SetMemoryManager(Old);
    GetMemoryManager(Back);
    SetMemoryManager(Back);
    Writeln('setting the current manager again is no switch ', not IsMemoryManagerSet);
    FreeMem(P);
  end
  else
  begin
    GetMem(P, 16);
    try
      SetMemoryManager(First);
      Writeln('not reached');
    except
      on E: Exception do
        Writeln(E.ClassName, ': ', E.Message);
    end;
    Writeln('manager unchanged ', not IsMemoryManagerSet);
    FreeMem(P);
  end;
  Writeln('main done');
end.
