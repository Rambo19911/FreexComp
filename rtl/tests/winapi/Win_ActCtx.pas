// covers: RTL-871, RTL-860, RTL-835
program Win_ActCtx;
{ The kernel32 functions of the run-time activation-context fallback (winapi.md RTL-871, section 5.1): the system
  directory from GetSystemDirectoryW, an activation context created by CreateActCtxW from manifest resource 124 of
  shell32.dll there, and ActivateActCtx on it. A resource that does not exist gives INVALID_HANDLE_VALUE. The
  context stays active until the process ends, as in the fallback. }
uses Windows, TestKit;

var
  Dir: array[0..MAX_PATH] of WideChar;
  Len: UINT;
  SysDir, Source: string;
  Ctx: TActCtxW;
  H, Missing: THandle;
  Cookie: ULONG_PTR;

begin
  Section('GetSystemDirectoryW');
  Len := GetSystemDirectoryW(@Dir[0], Length(Dir));
  Check((Len > 3) and (Len < Length(Dir)), 'length of the system directory');
  SysDir := Dir;
  CheckEqI(Len, Length(SysDir), 'the result is the length without the terminator');
  Check(SysDir[Length(SysDir)] <> '\', 'no trailing backslash');
  CheckEqI(0, GetSystemDirectoryW(nil, 0) - Len - 1, 'a too small buffer gives the size needed, with the terminator');

  Section('CreateActCtxW, ActivateActCtx');
  CheckEqI(56, SizeOf(TActCtxW), 'SizeOf(TActCtxW)');
  Source := SysDir + '\shell32.dll';
  FillChar(Ctx, SizeOf(Ctx), 0);
  Ctx.cbSize := SizeOf(Ctx);
  Ctx.dwFlags := ACTCTX_FLAG_RESOURCE_NAME_VALID or ACTCTX_FLAG_ASSEMBLY_DIRECTORY_VALID;
  Ctx.lpSource := PWideChar(Source);
  Ctx.lpAssemblyDirectory := PWideChar(SysDir);
  Ctx.lpResourceName := MakeIntResource(9999);
  Missing := CreateActCtxW(Ctx);
  Check(Missing = INVALID_HANDLE_VALUE, 'a missing manifest resource fails');
  Check(GetLastError <> 0, 'with an error code');
  Ctx.lpResourceName := MakeIntResource(124);
  H := CreateActCtxW(Ctx);
  Check(H <> INVALID_HANDLE_VALUE, 'manifest resource 124 of shell32.dll gives a context');
  Cookie := 0;
  Check(ActivateActCtx(H, Cookie), 'ActivateActCtx succeeds');
  Check(Cookie <> 0, 'and returns a cookie');
  Done;
end.
