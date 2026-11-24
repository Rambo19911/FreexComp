// phase: 8
// covers: RTL-950, RTL-956, RTL-957
// opc-run: variants 1 2 3
program Sys_LibraryLifeCycle;
{ The library life cycle of System (dll-runtime.md section 2, ABI-190 ... ABI-194) driven inside a program: the
  program plays the entry routine the compiler emits for a library and hands System the notifications of the
  operating system with a unit table of its own. Real libraries are the scenarios of rtl/tests/dll.
  Markers are written straight to the standard output handle, because the start-up sequence opens Output anew.
  Every Write statement flushes a standard file, so what the shutdown flush (ABI-192 step 3) has left to write is a
  pending half of a surrogate pair: it arrives as U+FFFD in front of the marker that follows the shutdown.
  Variant 1: attach, initialise, thread detach, process detach on unload (Reserved = nil).
  Variant 2: an initialization raises (ABI-193); process detach at process end (Reserved <> nil); a raising finalization.
  Variant 3: Halt(7) inside the library (RTL-957): shutdown steps 1-3, exit code 7, no leak report. }
uses Windows;

{$POINTERMATH ON}

type
  TFail = class
    destructor Destroy; override;
  end;

  TUnitEntry = record
    Init: procedure;
    Final: procedure;
  end;

threadvar
  PerThread: Integer;

var
  Table: array[0..2] of TUnitEntry;
  Lib: THandle;
  Which: WideChar;
  P: PChar;
  Leak: Pointer;
  Before: NativeInt;

procedure PutRaw(S: PAnsiChar; Len: Integer);
var
  Written: DWORD;
begin
  WriteFile(GetStdHandle(STD_OUTPUT_HANDLE), S, Len, Written, nil);
end;

procedure PutText(S: PAnsiChar);
var
  Len: Integer;
begin
  Len := 0;
  while S[Len] <> #0 do
    Inc(Len);
  PutRaw(S, Len);
end;

procedure Put(S: PAnsiChar);
begin
  PutText(S);
  PutRaw(#13#10, 2);
end;

procedure PutI(S: PAnsiChar; V: Int64);
var
  Digits: array[0..23] of AnsiChar;
  N: Integer;
  Negative: Boolean;
begin
  PutText(S);
  PutRaw(' ', 1);
  Negative := V < 0;
  if Negative then
    V := -V;
  N := 24;
  repeat
    Dec(N);
    Digits[N] := AnsiChar(48 + V mod 10);
    V := V div 10;
  until V = 0;
  if Negative then
    PutRaw('-', 1);
  PutRaw(@Digits[N], 24 - N);
  PutRaw(#13#10, 2);
end;

procedure PutB(S: PAnsiChar; B: Boolean);
begin
  PutText(S);
  if B then
    Put(' True')
  else
    Put(' False');
end;

destructor TFail.Destroy;
begin
  Put('exception object destroyed');
  inherited Destroy;
end;

procedure InitA;
begin
  Put('init A');
end;

procedure InitB;
begin
  Put('init B');
end;

procedure InitBFails;
begin
  Put('init B raises');
  raise TFail.Create;
end;

procedure InitC;
begin
  Put('init C');
end;

procedure FinalA;
begin
  Put('final A');
end;

procedure FinalAFails;
begin
  Put('final A raises');
  raise TFail.Create;
end;

procedure FinalB;
begin
  Put('final B');
end;

procedure FinalC;
begin
  Put('final C');
end;

procedure ExitHandler;
begin
  Put('exit procedure');
end;

procedure Unexpected(const Text: string);
begin
  Put('UNEXPECTED fatal text or leak report');
end;

{ a thread the module did not start: it uses the module's per-thread state, then the operating system tells the
  module that the thread ends }
function Worker(Parameter: Pointer): DWORD; stdcall;
begin
  SetInOutRes(5);
  PerThread := 1;
  Result := DWORD(_DllEntry(Lib, DLL_THREAD_DETACH, nil, nil, 0));
end;

{ the heap blocks such a thread leaves behind }
function BlocksLeftByAThread: NativeInt;
var
  Start: NativeInt;
  H: THandle;
  Id: DWORD;
begin
  Start := _LiveAllocCount;
  H := CreateThread(nil, 0, @Worker, nil, 0, @Id);
  WaitForSingleObject(H, INFINITE);
  CloseHandle(H);
  Result := _LiveAllocCount - Start;
end;

begin
  P := CmdLine;
  while P^ <> #0 do
    Inc(P);
  Dec(P);
  Which := P^;
  Lib := THandle(GetModuleHandleW('kernel32.dll'));   { any real module handle that is not the program's }

  PutB('program: IsLibrary', IsLibrary);
  PutI('not attached: thread attach returns', _DllEntry(Lib, DLL_THREAD_ATTACH, nil, nil, 0));
  PutI('not attached: process detach returns', _DllEntry(Lib, DLL_PROCESS_DETACH, nil, nil, 0));
  PutI('not attached: blocks left by a thread', BlocksLeftByAThread);

  Table[0].Init := InitA;
  Table[0].Final := FinalA;
  Table[1].Init := InitB;
  Table[1].Final := FinalB;
  Table[2].Init := InitC;
  Table[2].Final := FinalC;
  if Which = '2' then
  begin
    Table[1].Init := InitBFails;
    Table[0].Final := FinalAFails;
  end;
  PutI('process attach returns', _DllEntry(Lib, DLL_PROCESS_ATTACH, nil, @Table[0], 3));
  PutB('IsLibrary', IsLibrary);
  PutB('HInstance is the module handle', HInstance = Lib);
  PutB('MainThreadID is the loading thread', MainThreadID = GetCurrentThreadId);
  PutB('CmdLine is the host command line', CmdLine = GetCommandLineW);
  PutB('IsConsole of the host', IsConsole);
  PutI('ExitCode', ExitCode);
  PutI('second process attach returns', _DllEntry(Lib, DLL_PROCESS_ATTACH, nil, @Table[0], 3));
  PutI('initialising: blocks left by a thread', BlocksLeftByAThread);

  { a leak report or any other fatal text would arrive here }
  IsConsole := False;
  ShowErrorProc := Unexpected;
  ReportMemoryLeaksOnShutdown := True;
  GetMem(Leak, 8);

  case Which of
    '1':
      begin
        _InitUnits;
        _DllInitDone;
        PutI('attached: thread attach returns', _DllEntry(Lib, DLL_THREAD_ATTACH, nil, nil, 0));
        PutI('attached: blocks left by a thread', BlocksLeftByAThread);
        ExitProc := @ExitHandler;
        Writeln('text written through Output');
        Write(#$D83D);                  { half a surrogate pair waits in Output: U+FFFD when the shutdown flushes }
        Before := _LiveAllocCount;
        PutI('process detach returns', _DllEntry(Lib, DLL_PROCESS_DETACH, nil, nil, 0));
        PutI('blocks released at unload', Before - _LiveAllocCount);
        PutI('second process detach returns', _DllEntry(Lib, DLL_PROCESS_DETACH, nil, nil, 0));
        Put('end of program');
      end;
    '2':
      begin
        try
          _InitUnits;
          Put('not reached');
        except
          _DllInitFailed;                { the entry routine calls it after _BeginCatch }
        end;
        PutB('no handled exception left', ExceptObject = nil);
        PutI('init failed: blocks left by a thread', BlocksLeftByAThread);
        Writeln('text written through Output');
        Write(#$D83D);                  { half a surrogate pair waits in Output: U+FFFD when the shutdown flushes }
        Before := _LiveAllocCount;
        PutI('process detach returns', _DllEntry(Lib, DLL_PROCESS_DETACH, Pointer(1), nil, 0));
        PutI('blocks released at process end', Before - _LiveAllocCount);
        Put('end of program');
      end;
    '3':
      begin
        _InitUnits;
        _DllInitDone;
        ExitProc := @ExitHandler;
        Writeln('text written through Output');
        Write(#$D83D);                  { half a surrogate pair waits in Output: U+FFFD when the shutdown flushes }
        Halt(7);
        Put('not reached');
      end;
  end;
end.
