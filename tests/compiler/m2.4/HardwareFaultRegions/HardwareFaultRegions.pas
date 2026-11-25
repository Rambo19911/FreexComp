// covers: LANG-830, CMP-398
program HardwareFaultRegions;

{ hardware faults in the routine of the try statement: the locals are current in the handler, finally parts run,
  a region left by Break / Continue / Exit is closed, a fault in a handler ends that handler }

uses
  SysUtils, HardwareFaultUnit;

type
  IGuard = interface
  end;

  TGuard = class(TInterfacedObject, IGuard)
    FName: string;
    constructor Create(const Name: string);
    destructor Destroy; override;
  end;

constructor TGuard.Create(const Name: string);
begin
  inherited Create;
  FName := Name;
end;

destructor TGuard.Destroy;
begin
  Writeln('guard: released ', FName);
  inherited Destroy;
end;

{ a store the try part made before the fault is seen by the handler and after the statement }
procedure Locals;
var
  P: PInteger;
  X: Integer;
begin
  P := nil;
  X := 1;
  try
    X := 2;
    P^ := 7;
    X := 3;
  except
    on E: EAccessViolation do Writeln('locals: except X=', X);
  end;
  Writeln('locals: after X=', X);
end;

{ a read whose value is never used still faults }
procedure DeadRead;
var
  P: PInteger;
  X: Integer;
begin
  P := nil;
  try
    X := P^;
    Writeln('dead read: not reached');
  except
    Writeln('dead read: caught');
  end;
end;

procedure FinallyThenExcept;
var
  P: PInteger;
  Stage: Integer;
begin
  P := nil;
  Stage := 0;
  try
    try
      Stage := 1;
      P^ := 7;
      Stage := 2;
    finally
      Writeln('finally: stage ', Stage);
    end;
  except
    Writeln('finally: caught, stage ', Stage);
  end;
end;

{ Break and Continue close the inner region: the later fault belongs to the outer one }
procedure LeftByBreak;
var
  P: PInteger;
  I, N: Integer;
begin
  P := nil;
  N := 0;
  try
    for I := 1 to 5 do
      try
        if I = 2 then Continue;
        if I = 4 then Break;
        Inc(N);
      except
        Writeln('break: wrong handler');
      end;
    P^ := N;
    Writeln('break: not reached');
  except
    Writeln('break: outer caught, N=', N);
  end;
end;

function LeftByExit(P: PInteger): Integer;
begin
  Result := 1;
  try
    try
      Result := 2;
      if P = nil then Exit;
      Result := 3;
    finally
      Writeln('exit: finally');
    end;
  except
    Writeln('exit: wrong handler');
  end;
end;

procedure AfterExit;
var
  P: PInteger;
  R: Integer;
begin
  P := nil;
  try
    R := LeftByExit(P);
    Writeln('exit: result ', R);
    P^ := 1;
  except
    Writeln('exit: caller caught');
  end;
end;

{ a fault in a handler ends that handler (its exception object is destroyed) and reaches the outer try }
procedure InHandler;
var
  P: PInteger;
  Before: NativeInt;
begin
  P := nil;
  Before := _LiveAllocCount;
  try
    try
      P^ := 1;
    except
      Writeln('handler: first');
      P^ := 2;
      Writeln('handler: not reached');
    end;
  except
    on E: EAccessViolation do Writeln('handler: second');
  end;
  Writeln('handler: leaked ', _LiveAllocCount - Before);
end;

{ a fault in a loop: every iteration is caught and the loop goes on }
procedure InLoop;
var
  P: PInteger;
  I, Caught: Integer;
begin
  P := nil;
  Caught := 0;
  for I := 1 to 3 do
    try
      P^ := I;
    except
      Inc(Caught);
    end;
  Writeln('loop: caught ', Caught);
end;

{ the routine's own finalisation also covers what is raised before and after its try statement, and in a block of it }
procedure RaisesAround(Where: Integer);
var
  G: IGuard;
begin
  G := TGuard.Create('raise ' + IntToStr(Where));
  if Where = 1 then
    raise Exception.Create('before');
  try
    begin
      var Inner: IGuard := TGuard.Create('block');
      if Where = 2 then
        raise Exception.Create('inside');
    end;
  except
    on E: Exception do Writeln('guard: handler ', E.Message);
  end;
  if Where = 3 then
    raise Exception.Create('after');
end;

{ a fault outside every try statement of its routine (HardwareFaultUnit): the routine's managed variables are finalised }
procedure Guards;
var
  I: Integer;
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  try
    Faulting(nil);
  except
    on E: EAccessViolation do Writeln('guard: caught fault');
  end;
  Writeln('guard: leaked ', _LiveAllocCount - Before);
  for I := 1 to 3 do
    try
      RaisesAround(I);
    except
      on E: Exception do Writeln('guard: caught ', E.Message);
    end;
end;

begin
  Locals;
  DeadRead;
  FinallyThenExcept;
  LeftByBreak;
  AfterExit;
  InHandler;
  InLoop;
  Guards;
end.
