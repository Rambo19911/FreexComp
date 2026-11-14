// covers: LANG-823
program ExceptionDestroyed;

{ the exception object is destroyed after its handler completes, and before the jump when Exit, Break or Continue
  leaves the handler }

type
  ETracked = class
    Id: Integer;
    constructor Create(AId: Integer);
    destructor Destroy; override;
  end;

var
  Live: Integer;

constructor ETracked.Create(AId: Integer); begin Id := AId; Live := Live + 1 end;
destructor ETracked.Destroy; begin Live := Live - 1; Writeln('destroy ', Id); inherited end;

procedure Normal;
begin
  try
    raise ETracked.Create(1);
  except
    on E: ETracked do Writeln('handled ', E.Id, ', live ', Live);
  end;
  Writeln('after handler, live ', Live);
end;

procedure LeaveByExit;
begin
  try
    raise ETracked.Create(2);
  except
    Writeln('exit from handler');
    Exit;
  end;
  Writeln('not reached');
end;

procedure LeaveLoop;
var
  I: Integer;
begin
  for I := 3 to 5 do
    try
      raise ETracked.Create(I);
    except
      on E: ETracked do
      begin
        if E.Id = 3 then
        begin
          Writeln('continue from handler');
          Continue;
        end;
        Writeln('break from handler');
        Break;
      end;
    end;
  Writeln('after loop, live ', Live);
end;

var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  Normal;
  LeaveByExit;
  Writeln('after exit, live ', Live);
  LeaveLoop;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.