// covers: RTL-161, ABI-121, ABI-122, ABI-120
program Exc_Finally;
{ Unwinding through finally parts with System's exception runtime (R-062): finally order during unwind across
  routines, nested handlers inside finally parts, Exit / Break / Continue through finally parts, an exception that
  escapes a finally part during unwind (the abandoned one is destroyed, ABI-121 step 4) and one that is raised and
  caught inside a finally part during unwind (the propagating one is kept), and the per-thread stacks being empty
  afterwards (ExceptObject / RaiseList nil). Destructor counters and the RTL-064 leak counter. }
uses TestKit;

type
  ETrack = class
    Id: Integer;
    constructor Create(AId: Integer);
    destructor Destroy; override;
  end;

var
  Log: string;
  Live: Integer;

constructor ETrack.Create(AId: Integer);
begin
  Id := AId;
  Live := Live + 1;
end;

destructor ETrack.Destroy;
begin
  Live := Live - 1;
  Log := Log + '~' + IntStr(Id);
  inherited Destroy;
end;

procedure Inner;
var
  S: string;
begin
  S := 'managed' + IntStr(Live);
  try
    Log := Log + 'i';
    raise ETrack.Create(1);
  finally
    Log := Log + 'f1';
  end;
end;

procedure Middle;
begin
  try
    try
      Inner;
    finally
      Log := Log + 'f2';
    end;
  finally
    Log := Log + 'f3';
  end;
end;

{ in a routine of its own: the temporaries of the handler are released when it ends }
procedure OrderCase;
begin
  try
    Middle;
  except
    on E: ETrack do Log := Log + 'h' + IntStr(E.Id);
  end;
end;

procedure TestOrder;
var
  Before: NativeInt;
begin
  Section('finally order during unwind');
  Before := _LiveAllocCount;
  Log := '';
  OrderCase;
  CheckEqS('if1f2f3h1~1', Log, 'innermost first, handler, then destroy');
  CheckEqI(0, Live, 'live');
  Check(ExceptObject = nil, 'no handled exception left');
  Log := '';
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

procedure HandlerInFinally;
begin
  try
    raise ETrack.Create(2);
  finally
    try
      raise ETrack.Create(3);
    except
      on E: ETrack do Log := Log + 'n' + IntStr(E.Id);
    end;
    Log := Log + 'f';
  end;
end;

procedure TestHandlerInFinally;
begin
  Section('exception caught inside a finally part during unwind');
  Log := '';
  try
    HandlerInFinally;
  except
    on E: ETrack do Log := Log + 'h' + IntStr(E.Id);
  end;
  CheckEqS('n3~3fh2~2', Log, 'the propagating exception is kept');
  CheckEqI(0, Live, 'live');
end;

procedure EscapeFromFinally;
begin
  try
    raise ETrack.Create(4);
  finally
    Log := Log + 'f';
    raise ETrack.Create(5);
  end;
end;

procedure TestEscape;
var
  I: Integer;
begin
  Section('exception escaping a finally part');
  Log := '';
  try
    EscapeFromFinally;
  except
    on E: ETrack do Log := Log + 'h' + IntStr(E.Id);
  end;
  CheckEqS('f~4h5~5', Log, 'abandoned exception destroyed at the new filter');
  CheckEqI(0, Live, 'live');
  for I := 1 to 200 do
    try
      EscapeFromFinally;
    except
      on E: ETrack do ;
    end;
  CheckEqI(0, Live, 'live after many (no stale entries pile up)');
end;

function ExitThrough(N: Integer): Integer;
begin
  Result := 0;
  try
    try
      Result := N;
      if N > 0 then
        Exit;
      Result := -1;
    finally
      Log := Log + 'f' + IntStr(Result);
    end;
  except
    Log := Log + 'not reached';
  end;
end;

procedure TestJumps;
var
  I: Integer;
begin
  Section('Exit, Break, Continue through finally');
  Log := '';
  CheckEqI(7, ExitThrough(7), 'Exit keeps Result');
  CheckEqS('f7', Log, 'finally ran on Exit');
  Log := '';
  for I := 1 to 5 do
    try
      if I = 2 then
        Continue;
      if I = 4 then
        Break;
      Log := Log + IntStr(I);
    finally
      Log := Log + 'f';
    end;
  CheckEqS('1ff3ff', Log, 'Continue and Break run the finally part');
  Check(ExceptObject = nil, 'no exception state');
end;

procedure TestReraiseThroughFinally;
var
  Before: NativeInt;
begin
  Section('re-raise through finally');
  Before := _LiveAllocCount;
  Log := '';
  try
    try
      try
        raise ETrack.Create(6);
      except
        Log := Log + 'e';
        raise;
      end;
    finally
      Log := Log + 'f';
    end;
  except
    on E: ETrack do Log := Log + 'h' + IntStr(E.Id);
  end;
  CheckEqS('efh6~6', Log, 'destroyed once at the last handler');
  CheckEqI(0, Live, 'live');
  Check(RaiseList = nil, 'no handled entry left');
  Log := '';
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

begin
  TestOrder;
  TestHandlerInFinally;
  TestEscape;
  TestJumps;
  TestReraiseThroughFinally;
  Done;
end.
