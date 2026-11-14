// covers: LANG-622
program ClassDynamic;

{ 'dynamic' is exactly 'virtual' (LANG-622): it introduces a slot that override replaces; mixed with virtual
  methods in one class; overridden two levels down; called through ancestor references and from a static method. }

type
  TAnimal = class
    function Sound: string; dynamic;
    function Legs: Integer; virtual;
    procedure Show;
  end;

  TDog = class(TAnimal)
    function Sound: string; override;
  end;

  TPuppy = class(TDog)
    function Sound: string; override;
    function Legs: Integer; override;
  end;

  TBird = class(TAnimal)
    function Legs: Integer; override;
    procedure Fly; dynamic;
  end;

  TPenguin = class(TBird)
    procedure Fly; override;
  end;

function TAnimal.Sound: string;
begin
  Result := '...';
end;

function TAnimal.Legs: Integer;
begin
  Result := 4;
end;

procedure TAnimal.Show;
begin
  Writeln(Sound, ' ', Legs);
end;

function TDog.Sound: string;
begin
  Result := 'woof';
end;

function TPuppy.Sound: string;
begin
  Result := 'small ' + inherited Sound;
end;

function TPuppy.Legs: Integer;
begin
  Result := inherited Legs;
end;

function TBird.Legs: Integer;
begin
  Result := 2;
end;

procedure TBird.Fly;
begin
  Writeln('flap');
end;

procedure TPenguin.Fly;
begin
  Writeln('waddle');
end;

var
  A: TAnimal;
  B: TBird;
begin
  A := TAnimal.Create;
  A.Show;
  A.Free;
  A := TDog.Create;
  A.Show;
  A.Free;
  A := TPuppy.Create;
  A.Show;
  Writeln(A.Sound);
  A.Free;
  B := TBird.Create;
  B.Show;
  B.Fly;
  B.Free;
  B := TPenguin.Create;
  B.Fly;
  A := B;
  A.Show;
  A.Free;
end.
