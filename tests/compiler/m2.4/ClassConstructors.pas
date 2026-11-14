// covers: LANG-630, LANG-631, ABI-075
program ClassConstructors;

type
  TBase = class
    Name: string;
    Size: Integer;
    constructor Create;
    constructor CreateNamed(const AName: string);
    procedure Show;
  end;

  TDerived = class(TBase)
    Extra: Integer;
    constructor Create(AExtra: Integer);
    constructor CreateQuiet;
  end;

constructor TBase.Create;
begin
  inherited Create;
  Name := 'base';
  Size := 1;
  Writeln('TBase.Create');
end;

constructor TBase.CreateNamed(const AName: string);
begin
  Create;                                  { on the instance: body only, no new allocation }
  Name := AName;
  Writeln('TBase.CreateNamed ', AName);
end;

procedure TBase.Show;
begin
  Writeln(Name, ' ', Size);
end;

constructor TDerived.Create(AExtra: Integer);
begin
  inherited CreateNamed('derived');
  Extra := AExtra;
  if AExtra < 0 then
    Exit;                                  { still returns the instance }
  Size := Size + AExtra;
end;

constructor TDerived.CreateQuiet;
begin
  { no inherited call: fields stay zero (LANG-631) }
end;

var
  B: TBase;
  D: TDerived;
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  B := TBase.Create;
  B.Show;
  B.Free;
  B := TBase.CreateNamed('named');
  B.Show;
  B.Free;
  D := TDerived.Create(5);
  D.Show;
  Writeln(D.Extra);
  D.Free;
  D := TDerived.Create(-1);
  D.Show;
  D.Free;
  D := TDerived.CreateQuiet;
  Writeln(D.Name = '', ' ', D.Size, ' ', D.Extra);
  B := D;
  B.Free;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
