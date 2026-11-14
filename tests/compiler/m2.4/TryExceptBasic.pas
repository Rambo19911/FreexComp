// covers: LANG-805, LANG-807, LANG-810, LANG-821, LANG-823
program TryExceptBasic;

{ exceptions.md section 7 example Ex1, with the leak counter checked at the end }

type
  EMy = class
    Msg: string;
    constructor Create(const M: string);
    destructor Destroy; override;
  end;

constructor EMy.Create(const M: string); begin Msg := M end;
destructor EMy.Destroy; begin Writeln('destroy ', Msg); inherited end;

procedure Fail(n: Integer);
begin
  try
    if n > 0 then raise EMy.Create('boom');
    Writeln('no error');
  finally
    Writeln('finally ', n);
  end;
end;

var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  try
    Fail(0);
    Fail(1);
    Writeln('not reached');
  except
    on E: EMy do Writeln('caught ', E.Msg);
  end;
  try
    try
      raise EMy.Create('inner');
    except
      on E: EMy do begin Writeln('rethrow'); raise end;
    end;
  except
    Writeln('outer');
  end;
  Writeln('done');
  Writeln('leaked: ', _LiveAllocCount - Before);
end.