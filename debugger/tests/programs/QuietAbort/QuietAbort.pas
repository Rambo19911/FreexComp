program QuietAbort;
uses SysUtils;

type
  EQuiet = class(EAbort);
  EQuieter = class(EQuiet);

procedure Go;
begin
  try
    WriteLn('start');
    raise EQuiet.Create('quiet');
  except
    on E: EAbort do WriteLn('caught quiet');
  end;
  try
    raise EQuieter.Create('quieter');
  except
    on E: EAbort do WriteLn('caught quieter');
  end;
  try
    raise Exception.Create('loud');
  except
    on E: Exception do WriteLn('caught loud');
  end;
end;

begin
  Go;
end.
