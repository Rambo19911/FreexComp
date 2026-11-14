// covers: LANG-632, LANG-842, CMP-368, ABI-075, ABI-076
program ClassCtorException;

{ an exception escaping a constructor called on a class reference destroys the partial object through its virtual
  Destroy with Dealloc = 3 (no BeforeDestruction, memory released), then propagates; a constructor called through
  'inherited' only propagates. Nothing leaks. }

type
  EFail = class
    Where: string;
    constructor Create(const AWhere: string);
  end;

  TBase = class
    Name: string;
    constructor Create(FailAt: Integer);
    destructor Destroy; override;
    procedure AfterConstruction; override;
    procedure BeforeDestruction; override;
  end;

  TDerived = class(TBase)
    Extra: string;
    constructor Create(FailAt: Integer);
    destructor Destroy; override;
  end;

  TBaseClass = class of TBase;

constructor EFail.Create(const AWhere: string);
begin
  Where := AWhere;
end;

constructor TBase.Create(FailAt: Integer);
begin
  Name := 'base';
  Writeln('TBase.Create');
  if FailAt = 1 then
    raise EFail.Create('base');
end;

destructor TBase.Destroy;
begin
  Writeln('TBase.Destroy name=', Name);
  inherited;
end;

procedure TBase.AfterConstruction;
begin
  Writeln('AfterConstruction');
end;

procedure TBase.BeforeDestruction;
begin
  Writeln('BeforeDestruction');
end;

constructor TDerived.Create(FailAt: Integer);
begin
  inherited Create(FailAt);
  Extra := 'extra';
  Writeln('TDerived.Create');
  if FailAt = 2 then
    raise EFail.Create('derived');
end;

destructor TDerived.Destroy;
begin
  Writeln('TDerived.Destroy extra=', Extra);
  inherited;
end;

procedure Attempt(FailAt: Integer);
var
  O: TBase;
begin
  Writeln('-- derived, fail at ', FailAt);
  try
    O := TDerived.Create(FailAt);
    Writeln('created');
    O.Free;
  except
    on E: EFail do
      Writeln('caught ', E.Where);
  end;
end;

procedure AttemptByReference(C: TBaseClass; FailAt: Integer);
var
  O: TBase;
begin
  Writeln('-- class reference, fail at ', FailAt);
  try
    O := C.Create(FailAt);
    Writeln('created');
    O.Free;
  except
    on E: EFail do
      Writeln('caught ', E.Where);
  end;
end;

var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  Attempt(0);
  Attempt(1);
  Attempt(2);
  AttemptByReference(TDerived, 1);
  AttemptByReference(TBase, 1);
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
