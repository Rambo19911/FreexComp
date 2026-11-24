program PlatGui;

{ Platform check fixture: a window program that creates no window. It writes 'dpi=1' to the file named by its
  first argument when its thread runs per-monitor (version 2) DPI aware, else 'dpi=0', and exits. }

const
  { DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 }
  PerMonitorAwareV2 = NativeInt(-4);

function GetThreadDpiAwarenessContext: NativeInt; stdcall; external 'user32.dll';
function AreDpiAwarenessContextsEqual(A, B: NativeInt): LongBool; stdcall; external 'user32.dll';

var
  F: TextFile;
begin
  AssignFile(F, ParamStr(1));
  Rewrite(F);
  if AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext, PerMonitorAwareV2) then
    Writeln(F, 'dpi=1')
  else
    Writeln(F, 'dpi=0');
  CloseFile(F);
end.
