// covers: RTL-1065
// phase: 8
program Sys_MemoryManagerStdFiles;
{ The buffers of the standard text files belong to the runtime: they come from the default manager's own routines
  and go back the same way, so a manager installed by the first unit initialization (MemProbeKit) is never asked to
  release a block it did not hand out - not when a standard file is closed, and not for a file the program opens
  itself. }
uses MemProbeKit;

var
  S: string;
  Live: NativeInt;
begin
  Live := _LiveAllocCount;
  CloseFile(Input);
  CloseFile(ErrOutput);
  Writeln('default manager blocks released by closing two standard files: ', Live - _LiveAllocCount);
  S := StringOfChar('x', 40);
  S := S + S;
  S := '';
  Writeln('foreign releases: ', ForeignReleases);
end.
