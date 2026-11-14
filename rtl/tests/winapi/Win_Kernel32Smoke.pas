// covers: RTL-820, RTL-821, RTL-823, RTL-825, RTL-860, RTL-862
program Win_Kernel32Smoke;
{ Windows unit, kernel32 + ntdll part (winapi.md §3, §5.1, §5.2): base type widths, the macros of RTL-825, and
  a call of the process, heap, error, console, time, text and thread functions with results that do not depend
  on the machine. File functions: Win_Kernel32Files. }
uses Windows, TestKit;

{$POINTERMATH ON}

var
  Counter: Integer;
  Lock: TRTLCriticalSection;

function AddThread(lpParameter: Pointer): DWORD; stdcall;
var
  I: Integer;
begin
  for I := 1 to 1000 do
  begin
    EnterCriticalSection(Lock);
    Counter := Counter + 1;
    LeaveCriticalSection(Lock);
  end;
  Result := DWORD(NativeUInt(lpParameter));
end;

function ExitingThread(lpParameter: Pointer): DWORD; stdcall;
begin
  ExitThread(7);
  Result := 1;
end;

procedure BaseTypes;
begin
  Section('base types');
  CheckEqI(4, SizeOf(BOOL), 'BOOL');
  CheckEqI(4, SizeOf(DWORD), 'DWORD');
  CheckEqI(4, SizeOf(UINT), 'UINT');
  CheckEqI(4, SizeOf(LONG), 'LONG');
  CheckEqI(2, SizeOf(WORD), 'WORD');
  CheckEqI(8, SizeOf(ULONG_PTR), 'ULONG_PTR');
  CheckEqI(8, SizeOf(SIZE_T), 'SIZE_T');
  CheckEqI(8, SizeOf(WPARAM), 'WPARAM');
  CheckEqI(8, SizeOf(LPARAM), 'LPARAM');
  CheckEqI(8, SizeOf(LRESULT), 'LRESULT');
  CheckEqI(8, SizeOf(HANDLE), 'HANDLE');
  CheckEqI(8, SizeOf(HWND), 'HWND');
  CheckEqI(8, SizeOf(HMODULE), 'HMODULE');
  CheckEqI(8, SizeOf(LONGLONG), 'LONGLONG');
  CheckEqI(4, SizeOf(COLORREF), 'COLORREF');
  CheckEqI(4, SizeOf(NTSTATUS), 'NTSTATUS');
end;

procedure Macros;
begin
  Section('macros');
  CheckEqI($56781234, MakeLong($1234, $5678), 'MakeLong');
  CheckEqI($BEEF, LoWord($DEADBEEF), 'LoWord');
  CheckEqI($DEAD, HiWord($DEADBEEF), 'HiWord');
  CheckEqI($DEAD, HiWord($12345678DEADBEEF), 'HiWord bits 16..31');
  CheckEqI($00020001, MakeWParam(1, 2), 'MakeWParam');
  CheckEqI($00040003, MakeLParam(3, 4), 'MakeLParam');
  CheckEqI($00060005, MakeLResult(5, 6), 'MakeLResult');
  CheckEqI($00332211, RGB($11, $22, $33), 'RGB');
  CheckEqI($11, GetRValue($00332211), 'GetRValue');
  CheckEqI($22, GetGValue($00332211), 'GetGValue');
  CheckEqI($33, GetBValue($00332211), 'GetBValue');
  CheckEqI(5, NativeInt(MakeIntResource(5)), 'MakeIntResource');
  CheckEqB(True, Succeeded(S_OK), 'Succeeded');
  CheckEqB(True, Failed(HRESULT($80004005)), 'Failed');
  CheckEqI(-1, GET_X_LPARAM(LPARAM($0002FFFF)), 'GET_X_LPARAM');
  CheckEqI(-2, GET_Y_LPARAM(LPARAM($FFFE0001)), 'GET_Y_LPARAM');
  CheckEqI(-120, GET_WHEEL_DELTA_WPARAM(MakeWParam(0, Word(-120))), 'GET_WHEEL_DELTA_WPARAM');
end;

procedure Process;
var
  Info: TSystemInfo;
  Version: TOSVersionInfoExW;
  Buffer: array[0..MAX_PATH] of WideChar;
  N: DWORD;
begin
  Section('process');
  Check(GetCurrentProcessId <> 0, 'GetCurrentProcessId');
  CheckEqI(-1, NativeInt(GetCurrentProcess), 'GetCurrentProcess pseudo handle');
  CheckEqI(Int64(HInstance), Int64(GetModuleHandleW(nil)), 'GetModuleHandleW(nil)');
  CheckEqI(Int64(HInstance), Int64(GetModuleHandle(nil)), 'GetModuleHandle alias');
  N := GetModuleFileNameW(0, @Buffer[0], MAX_PATH);
  Check((N > 4) and (Buffer[N - 4] = '.') and (Buffer[N] = #0), 'GetModuleFileNameW');
  Check(GetCommandLineW <> nil, 'GetCommandLineW');
  Check(SetEnvironmentVariableW('FREEXCOMP_SMOKE', 'v=1'), 'SetEnvironmentVariableW');
  N := GetEnvironmentVariableW('FREEXCOMP_SMOKE', @Buffer[0], MAX_PATH);
  CheckEqI(3, N, 'GetEnvironmentVariableW length');
  CheckEqS('v=1', string(PWideChar(@Buffer[0])), 'GetEnvironmentVariableW value');
  SetEnvironmentVariableW('FREEXCOMP_SMOKE', nil);
  CheckEqI(0, GetEnvironmentVariableW('FREEXCOMP_SMOKE', @Buffer[0], MAX_PATH), 'variable removed');
  CheckEqI(ERROR_ENVVAR_NOT_FOUND, GetLastError, 'ERROR_ENVVAR_NOT_FOUND');
  IsDebuggerPresent;
  OutputDebugStringW('Win_Kernel32Smoke');
  GetSystemInfo(Info);
  CheckEqI(PROCESSOR_ARCHITECTURE_AMD64, Info.wProcessorArchitecture, 'GetSystemInfo architecture');
  CheckEqI(4096, Info.dwPageSize, 'GetSystemInfo page size');
  Check(Info.dwNumberOfProcessors >= 1, 'GetSystemInfo processors');
  CheckEqI(65536, Info.dwAllocationGranularity, 'GetSystemInfo granularity');
  CheckEqI(8, MulDiv(10, 3, 4), 'MulDiv rounds');
  CheckEqI(-1, MulDiv(1, 1, 0), 'MulDiv by zero');
  FillChar(Version, SizeOf(Version), 0);
  Version.dwOSVersionInfoSize := SizeOf(Version);
  CheckEqI(STATUS_SUCCESS, RtlGetVersion(POSVersionInfoW(@Version)), 'RtlGetVersion');
  CheckEqI(10, Version.dwMajorVersion, 'RtlGetVersion major');
  Check(Version.dwBuildNumber >= 17763, 'RtlGetVersion build >= 1809');
  CheckEqI(VER_PLATFORM_WIN32_NT, Version.dwPlatformId, 'RtlGetVersion platform');
end;

procedure Memory;
var
  Heap: THandle;
  P: PByte;
  Text: PWideChar;
  G: HGLOBAL;
begin
  Section('memory and errors');
  Heap := GetProcessHeap;
  Check(Heap <> 0, 'GetProcessHeap');
  P := HeapAlloc(Heap, HEAP_ZERO_MEMORY, 100);
  Check((P <> nil) and (P[99] = 0), 'HeapAlloc zeroed');
  CheckEqI(100, HeapSize(Heap, 0, P), 'HeapSize');
  P[0] := 77;
  P := HeapReAlloc(Heap, 0, P, 5000);
  Check((P <> nil) and (P[0] = 77), 'HeapReAlloc keeps content');
  Check(HeapFree(Heap, 0, P), 'HeapFree');
  P := VirtualAlloc(nil, 8192, MEM_COMMIT or MEM_RESERVE, PAGE_READWRITE);
  Check((P <> nil) and (P[8191] = 0), 'VirtualAlloc committed zero pages');
  P[8191] := 1;
  Check(VirtualFree(P, 0, MEM_RELEASE), 'VirtualFree');
  SetLastError(1234);
  CheckEqI(1234, GetLastError, 'SetLastError/GetLastError');
  Text := nil;
  Check(FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER or FORMAT_MESSAGE_FROM_SYSTEM or FORMAT_MESSAGE_IGNORE_INSERTS,
    nil, ERROR_FILE_NOT_FOUND, 0, PWideChar(@Text), 0, nil) > 0, 'FormatMessageW');
  Check(Text <> nil, 'FormatMessageW allocated');
  Check(LocalFree(HLOCAL(Text)) = 0, 'LocalFree');
  G := GlobalAlloc(GMEM_MOVEABLE or GMEM_ZEROINIT, 64);
  Check(G <> 0, 'GlobalAlloc');
  P := GlobalLock(G);
  Check((P <> nil) and (P[63] = 0), 'GlobalLock zeroed');
  P[0] := 5;
  Check(not GlobalUnlock(G), 'GlobalUnlock (lock count 0)');
  CheckEqI(0, GetLastError, 'GlobalUnlock NO_ERROR');
  P := GlobalLock(G);
  Check((P <> nil) and (P[0] = 5), 'GlobalLock keeps content');
  GlobalUnlock(G);
  Check(GlobalFree(G) = 0, 'GlobalFree');
end;

procedure Console;
var
  Out: THandle;
  N: DWORD;
  Mode: DWORD;
  Buffer: array[0..3] of WideChar;
begin
  Section('std handles');
  Out := GetStdHandle(STD_OUTPUT_HANDLE);
  Check((Out <> 0) and (Out <> INVALID_HANDLE_VALUE), 'GetStdHandle');
  CheckEqI(FILE_TYPE_PIPE, GetFileType(Out), 'GetFileType pipe');
  CheckEqB(False, GetConsoleMode(Out, Mode), 'GetConsoleMode on a pipe');
  CheckEqB(False, WriteConsoleW(Out, @Buffer[0], 0, N, nil), 'WriteConsoleW on a pipe');
  CheckEqB(False, ReadConsoleW(GetStdHandle(STD_INPUT_HANDLE), @Buffer[0], 4, N, nil), 'ReadConsoleW on a pipe');
end;

procedure Time;
var
  ST, ST2: TSystemTime;
  FT, Local, Back: TFileTime;
  TZ: TTimeZoneInformation;
  Freq, Count: Int64;
begin
  Section('time');
  GetSystemTime(ST);
  Check(ST.wYear >= 2024, 'GetSystemTime');
  GetLocalTime(ST2);
  Check((ST2.wMonth >= 1) and (ST2.wMonth <= 12), 'GetLocalTime');
  GetSystemTimeAsFileTime(FT);
  Check(FT.dwHighDateTime > 0, 'GetSystemTimeAsFileTime');
  ST.wYear := 2001; ST.wMonth := 2; ST.wDay := 3; ST.wHour := 4; ST.wMinute := 5; ST.wSecond := 6;
  ST.wMilliseconds := 7;
  Check(SystemTimeToFileTime(ST, FT), 'SystemTimeToFileTime');
  Check(FileTimeToSystemTime(FT, ST2), 'FileTimeToSystemTime');
  CheckEqI(2001, ST2.wYear, 'round trip year');
  CheckEqI(6, ST2.wDayOfWeek, 'round trip day of week');
  CheckEqI(7, ST2.wMilliseconds, 'round trip milliseconds');
  Check(FileTimeToLocalFileTime(FT, Local), 'FileTimeToLocalFileTime');
  Check(LocalFileTimeToFileTime(Local, Back), 'LocalFileTimeToFileTime');
  Check((Back.dwLowDateTime = FT.dwLowDateTime) and (Back.dwHighDateTime = FT.dwHighDateTime), 'local round trip');
  Check(GetTimeZoneInformation(TZ) <> TIME_ZONE_ID_INVALID, 'GetTimeZoneInformation');
  Check(GetTickCount64 > 0, 'GetTickCount64');
  GetTickCount;
  Check(QueryPerformanceFrequency(Freq) and (Freq > 0), 'QueryPerformanceFrequency');
  Check(QueryPerformanceCounter(Count) and (Count > 0), 'QueryPerformanceCounter');
  Sleep(0);
end;

procedure TextAndLocale;
const
  Utf8: array[0..2] of Byte = ($C3, $A9, $41);
var
  W: array[0..7] of WideChar;
  B: array[0..7] of Byte;
  N: Integer;
begin
  Section('text and locale');
  N := MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, @Utf8[0], 3, @W[0], 8);
  Check((N = 2) and (W[0] = #$E9) and (W[1] = 'A'), 'MultiByteToWideChar');
  N := WideCharToMultiByte(CP_UTF8, 0, @W[0], 2, @B[0], 8, nil, nil);
  Check((N = 3) and (B[0] = $C3) and (B[1] = $A9) and (B[2] = $41), 'WideCharToMultiByte');
  N := GetLocaleInfoEx(LOCALE_NAME_INVARIANT, LOCALE_SDECIMAL, @W[0], 8);
  Check((N = 2) and (W[0] = '.'), 'GetLocaleInfoEx invariant decimal');
  CheckEqI(CSTR_EQUAL, CompareStringEx(LOCALE_NAME_INVARIANT, NORM_IGNORECASE, 'abc', 3, 'ABC', 3, nil, nil, 0),
    'CompareStringEx ignore case');
  CheckEqI(CSTR_LESS_THAN, CompareStringEx(LOCALE_NAME_INVARIANT, 0, 'a', 1, 'b', 1, nil, nil, 0), 'CompareStringEx less');
end;

procedure Threads;
var
  H: array[0..1] of THandle;
  Id, Code, Previous: DWORD;
  Ev, Mutex, Sem: THandle;
  Slot: DWORD;
begin
  Section('threads and synchronisation');
  Check(InitializeCriticalSectionAndSpinCount(Lock, 4000), 'InitializeCriticalSectionAndSpinCount');
  H[0] := CreateThread(nil, 0, @AddThread, Pointer(42), CREATE_SUSPENDED, @Id);
  H[1] := CreateThread(nil, 0, @AddThread, Pointer(43), 0, nil);
  Check((H[0] <> 0) and (Id <> 0), 'CreateThread');
  CheckEqI(1, ResumeThread(H[0]), 'ResumeThread previous count');
  CheckEqI(WAIT_OBJECT_0, WaitForMultipleObjects(2, @H[0], True, INFINITE), 'WaitForMultipleObjects');
  CheckEqI(2000, Counter, 'critical section counter');
  Check(GetExitCodeThread(H[0], Code) and (Code = 42), 'GetExitCodeThread');
  CloseHandle(H[0]);
  CloseHandle(H[1]);
  H[0] := CreateThread(nil, 0, @ExitingThread, nil, 0, nil);
  CheckEqI(WAIT_OBJECT_0, WaitForSingleObject(H[0], INFINITE), 'WaitForSingleObject');
  Check(GetExitCodeThread(H[0], Code) and (Code = 7), 'ExitThread code');
  CloseHandle(H[0]);
  Check(TryEnterCriticalSection(Lock), 'TryEnterCriticalSection');
  LeaveCriticalSection(Lock);
  DeleteCriticalSection(Lock);
  Check(GetCurrentThreadId = MainThreadID, 'GetCurrentThreadId');
  CheckEqI(-2, NativeInt(GetCurrentThread), 'GetCurrentThread pseudo handle');
  SwitchToThread;
  Slot := TlsAlloc;
  Check(Slot <> TLS_OUT_OF_INDEXES, 'TlsAlloc');
  Check(TlsSetValue(Slot, Pointer(99)) and (TlsGetValue(Slot) = Pointer(99)), 'TlsSetValue/TlsGetValue');
  Check(TlsFree(Slot), 'TlsFree');
  Ev := CreateEventW(nil, True, False, nil);
  CheckEqI(WAIT_TIMEOUT, WaitForSingleObject(Ev, 0), 'event not signalled');
  Check(SetEvent(Ev), 'SetEvent');
  CheckEqI(WAIT_OBJECT_0, WaitForSingleObject(Ev, 0), 'event signalled');
  Check(ResetEvent(Ev), 'ResetEvent');
  CheckEqI(WAIT_TIMEOUT, WaitForSingleObject(Ev, 0), 'event reset');
  CloseHandle(Ev);
  Mutex := CreateMutexW(nil, True, nil);
  Check(ReleaseMutex(Mutex), 'CreateMutexW/ReleaseMutex');
  CheckEqB(False, ReleaseMutex(Mutex), 'ReleaseMutex not owned');
  CloseHandle(Mutex);
  Sem := CreateSemaphoreW(nil, 1, 5, nil);
  Check(ReleaseSemaphore(Sem, 2, @Previous) and (Previous = 1), 'CreateSemaphoreW/ReleaseSemaphore');
  CloseHandle(Sem);
end;

begin
  BaseTypes;
  Macros;
  Process;
  Memory;
  Console;
  Time;
  TextAndLocale;
  Threads;
  Done;
end.
