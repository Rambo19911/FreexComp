// covers: LANG-721, LANG-722, CMP-380
program AnonInCtor;

type
  TProc = reference to procedure;
  TIntFunc = reference to function: Integer;

  TCounter = class
    FCount: Integer;
    FGet: TIntFunc;
    constructor Create(Start: Integer);
    destructor Destroy; override;
  end;

  TDerived = class(TCounter)
    constructor Create;
  end;

  TPt = record
    X, Y: Integer;
    constructor Create(AX: Integer);
  end;

constructor TCounter.Create(Start: Integer);
var
  Bump: TProc;
  Step: Integer;
begin
  inherited Create;
  Step := 2;
  Bump := procedure begin FCount := FCount + Step end;
  FCount := Start;
  Bump();
  Bump();
  FGet := function: Integer begin Result := FCount * 10 + Step end;
  Step := 3;
end;

destructor TCounter.Destroy;
var
  Say: TProc;
begin
  Say := procedure begin Writeln('destroying ', FCount) end;
  Say();
  inherited;
end;

constructor TDerived.Create;
var
  Twice: TProc;
begin
  inherited Create(10);
  Twice := procedure begin FCount := FCount * 2 end;
  Twice();
end;

constructor TPt.Create(AX: Integer);
var
  Say: TProc;
begin
  Say := procedure begin Writeln('rec ', AX) end;
  Say();
  X := AX;
  Y := AX + 1;
end;

procedure Run;
var
  C: TCounter;
  P: TPt;
begin
  C := TCounter.Create(1);
  Writeln(C.FCount);
  Writeln(C.FGet());
  C.FCount := 6;
  Writeln(C.FGet());
  C.Free;
  C := TDerived.Create;
  Writeln(C.FCount, ' ', C.FGet());
  C.Free;
  P := TPt.Create(4);
  Writeln(P.X, ' ', P.Y);
end;

var
  Before: Integer;
begin
  Before := _LiveAllocCount;
  Run;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
