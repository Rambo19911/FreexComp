// covers: LANG-602, LANG-622, CMP-366
program ClassAbstract;

{ Abstract methods overridden in descendants (an abstract procedure and an abstract function, one of them
  overridden only one level further down); an abstract method called from a static method of the abstract base;
  a 'class abstract' whose descendants are created. No W1061: every created class overrides all abstract
  methods (the empty .diag file checks that opc reports nothing). }

type
  TCounter = class
    Count: Integer;
    procedure Step; virtual; abstract;
    function Name: string; virtual; abstract;
    procedure Run(N: Integer);
  end;

  TByOne = class(TCounter)
    procedure Step; override;
    function Name: string; override;
  end;

  TByTwo = class(TCounter)
    procedure Step; override;
  end;

  TByTwoNamed = class(TByTwo)
    function Name: string; override;
  end;

  TBase = class abstract
    function Value: Integer; virtual;
  end;

  TConcrete = class(TBase)
    function Value: Integer; override;
  end;

procedure TCounter.Run(N: Integer);
var
  I: Integer;
begin
  for I := 1 to N do
    Step;
  Writeln(Name, ' ', Count);
end;

procedure TByOne.Step;
begin
  Count := Count + 1;
end;

function TByOne.Name: string;
begin
  Result := 'one';
end;

procedure TByTwo.Step;
begin
  Count := Count + 2;
end;

function TByTwoNamed.Name: string;
begin
  Result := 'two';
end;

function TBase.Value: Integer;
begin
  Result := 1;
end;

function TConcrete.Value: Integer;
begin
  Result := inherited Value + 41;
end;

var
  C: TCounter;
  B: TBase;
begin
  C := TByOne.Create;
  C.Run(3);
  C.Free;
  C := TByTwoNamed.Create;
  C.Run(3);
  C.Step;
  Writeln(C.Count);
  C.Free;
  B := TConcrete.Create;
  Writeln(B.Value);
  B.Free;
end.
