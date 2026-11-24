// covers: RTL-958, RTL-964
program usefail;
// an exception escapes a unit initialization of the library: the load fails, the units initialised so far are
// finalised, the run-time library of the library reports nothing, and the host goes on

uses Windows, SysUtils;

const
  ErrorFile = 'stderr.txt';

// the lines of the file that was the standard error handle while the library loaded
procedure PrintErrorFile;
var
  F: Text;
  Line: string;
  Count: Integer;
begin
  Count := 0;
  Assign(F, ErrorFile);
  Reset(F);
  while not Eof(F) do
  begin
    Readln(F, Line);
    Count := Count + 1;
    Writeln('  standard error line ', Count, ': ', Line);
  end;
  Close(F);
  Writeln('lines on standard error during the load: ', Count);
end;

var
  H: HMODULE;
  Capture, Saved: THandle;
  Error: DWORD;
begin
  Writeln('loading');
  Flush(Output);
  Capture := CreateFileW(ErrorFile, GENERIC_WRITE, FILE_SHARE_READ, nil, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, 0);
  Saved := GetStdHandle(STD_ERROR_HANDLE);
  SetStdHandle(STD_ERROR_HANDLE, Capture);
  H := LoadLibraryW('faillib.dll');
  Error := GetLastError;
  SetStdHandle(STD_ERROR_HANDLE, Saved);
  CloseHandle(Capture);

  Writeln('LoadLibraryW returned 0: ', H = 0);
  Writeln('last error: ', Error);
  Writeln('module not loaded: ', GetModuleHandleW('faillib.dll') = 0);
  Writeln('first unit initialised: ', FileExists('first-init.txt'));
  Writeln('first unit finalised: ', FileExists('first-final.txt'));
  Writeln('failing unit not finalised: ', not FileExists('second-final.txt'));
  Writeln('library block not run: ', not FileExists('library-block.txt'));
  PrintErrorFile;
  try
    raise Exception.Create('host exceptions still work');
  except
    on E: Exception do
      Writeln('caught: ', E.Message);
  end;
  Writeln('host continues');
end.
