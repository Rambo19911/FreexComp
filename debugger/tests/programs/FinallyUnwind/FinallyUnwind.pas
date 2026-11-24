program FinallyUnwind;

uses
  SysUtils;

procedure Fail(Code: Integer);
begin
  raise Exception.Create('fail ' + IntToStr(Code));
end;

procedure Run(N: Integer);
var
  Total: Integer;
begin
  Total := N;
  try
    Total := Total + 1;
    Fail(Total);
  finally
    var Fin: Integer := Total * 10;
    WriteLn(Fin);
  end;
  WriteLn(Total);
end;

begin
  try
    Run(3);
  except
    on E: Exception do
      WriteLn(E.Message);
  end;
end.
