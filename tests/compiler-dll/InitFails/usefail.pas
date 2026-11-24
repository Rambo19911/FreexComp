// covers: CMP-1051
program usefail;
// an exception that escapes the initialisation of a library makes the load fail; the host goes on

uses Windows, SysUtils;

var
  H: HMODULE;
begin
  H := LoadLibraryW('faillib.dll');
  Writeln('LoadLibraryW returned 0: ', H = 0);
  Writeln('first unit initialised: ', FileExists('first-init.txt'));
  Writeln('first unit finalised: ', FileExists('first-final.txt'));
  Writeln('failing unit not finalised: ', not FileExists('bad-final.txt'));
  Writeln('library block not run: ', not FileExists('library-block.txt'));
  try
    raise Exception.Create('host exceptions still work');
  except
    on E: Exception do
      Writeln('caught: ', E.Message);
  end;
  Writeln('host continues');
end.
