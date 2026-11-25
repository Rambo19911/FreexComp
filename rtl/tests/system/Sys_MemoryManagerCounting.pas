// covers: RTL-1061, RTL-1065
// phase: 8
program Sys_MemoryManagerCounting;
{ A counting manager installed and removed by a unit (MemCountKit, first in the uses clause): the program creates
  and frees 100 objects and 100 strings; the unit's finalization prints that it saw at least 200 requests and as
  many releases as requests. }
uses MemCountKit;

procedure Work;
var
  Objects: array[0..99] of TObject;
  Strings: array[0..99] of string;
  I: Integer;
begin
  for I := 0 to 99 do
  begin
    Objects[I] := TObject.Create;
    Strings[I] := StringOfChar('s', I + 1);
  end;
  for I := 0 to 99 do
  begin
    Objects[I].Free;
    Strings[I] := '';
  end;
end;

begin
  Writeln('installed: ', IsMemoryManagerSet);
  Work;
  Writeln('main done');
end.
