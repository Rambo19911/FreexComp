// covers: LANG-823, LANG-825, ABI-121
program TryNested;

{ an exception raised and handled inside a handler or a finally part does not disturb the one in progress; a new
  exception leaving a handler replaces the one being handled }

type
  ETagged = class
    Tag: string;
    constructor Create(const T: string);
    destructor Destroy; override;
  end;

constructor ETagged.Create(const T: string); begin Tag := T end;
destructor ETagged.Destroy; begin Writeln('destroy ', Tag); inherited end;

procedure HandledInsideFinally;
begin
  try
    raise ETagged.Create('outer');
  finally
    try
      raise ETagged.Create('in finally');
    except
      on E: ETagged do Writeln('finally handled ', E.Tag);
    end;
    Writeln('finally done');
  end;
end;

var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  try
    raise ETagged.Create('first');
  except
    on E: ETagged do
    begin
      try
        raise ETagged.Create('nested');
      except
        on N: ETagged do Writeln('nested handled ', N.Tag, ' while handling ', E.Tag);
      end;
      Writeln('still handling ', E.Tag, ', ExceptObject = ', ETagged(ExceptObject).Tag);
    end;
  end;
  try
    try
      raise ETagged.Create('old');
    except
      raise ETagged.Create('new');
    end;
  except
    on E: ETagged do Writeln('caught ', E.Tag);
  end;
  try
    HandledInsideFinally;
  except
    on E: ETagged do Writeln('caught ', E.Tag);
  end;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.