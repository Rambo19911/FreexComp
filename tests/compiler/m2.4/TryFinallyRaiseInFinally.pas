// covers: LANG-811, ABI-121
program TryFinallyRaiseInFinally;

{ a finally part that raises while an exception propagates: the new exception replaces the old one, which is destroyed;
  also when the handler is in the same routine as the finally part (C-091: a finally part that always raises) }

type
  ENamed = class
    Name: string;
    constructor Create(const N: string);
    destructor Destroy; override;
  end;

constructor ENamed.Create(const N: string); begin Name := N end;
destructor ENamed.Destroy; begin Writeln('destroy ', Name); inherited end;

procedure Work;
begin
  try
    raise ENamed.Create('first');
  finally
    Writeln('finally raises second');
    raise ENamed.Create('second');
  end;
end;

procedure SameRoutine;
begin
  try
    try
      raise ENamed.Create('third');
    finally
      Writeln('finally raises fourth');
      raise ENamed.Create('fourth');
    end;
  except
    on E: ENamed do Writeln('same routine caught ', E.Name);
  end;
end;

var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  try
    Work;
  except
    on E: ENamed do Writeln('caught ', E.Name);
  end;
  SameRoutine;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.