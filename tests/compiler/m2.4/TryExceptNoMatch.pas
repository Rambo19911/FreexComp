// covers: LANG-821, LANG-810
program TryExceptNoMatch;

{ no handler matches: the exception propagates (as if re-raised) through a finally part to the outer handler }

type
  EA = class
    destructor Destroy; override;
  end;

  EB = class
  end;

destructor EA.Destroy; begin Writeln('destroy EA'); inherited end;

procedure Inner;
begin
  try
    Writeln('inner raises EA');
    raise EA.Create;
  except
    on EB do Writeln('not reached: EB');
  end;
  Writeln('not reached: after the inner try');
end;

procedure Middle;
begin
  try
    Inner;
  finally
    Writeln('middle finally');
  end;
end;

var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  try
    Middle;
  except
    on E: EA do Writeln('outer caught ', E.ClassName);
  end;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.