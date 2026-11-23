program Raises;
uses SysUtils;

type
  EFirst = class(Exception);
  ESecond = class(Exception);

procedure Go;
begin
  try
    WriteLn('start');
    raise EFirst.Create('first');
  except
    on E: EFirst do WriteLn('caught first');
  end;
  try
    raise ESecond.Create('second');
  except
    on E: ESecond do WriteLn('caught second');
  end;
end;

begin
  Go;
end.