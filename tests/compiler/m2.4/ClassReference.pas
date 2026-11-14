// covers: LANG-630, LANG-633, LANG-652, CMP-367
program ClassReference;

{ class of: class names as values, a virtual constructor called through a class-reference variable, parameter and
  array element (dispatches on the run-time class), on a class name (static), ClassName / ClassType / ClassParent /
  InheritsFrom / InstanceSize, = and <> on class references, nil. }

type
  TAnimal = class
    constructor Create; virtual;
    function Sound: string; virtual;
  end;

  TAnimalClass = class of TAnimal;

  TDog = class(TAnimal)
    constructor Create; override;
    function Sound: string; override;
  end;

  TPuppy = class(TDog)
    function Sound: string; override;
  end;

constructor TAnimal.Create;
begin
  inherited Create;
  Writeln('animal created');
end;

function TAnimal.Sound: string;
begin
  Result := '...';
end;

constructor TDog.Create;
begin
  inherited Create;
  Writeln('dog created');
end;

function TDog.Sound: string;
begin
  Result := 'woof';
end;

function TPuppy.Sound: string;
begin
  Result := 'yip';
end;

function Make(C: TAnimalClass): TAnimal;
begin
  Result := C.Create;
end;

var
  C: TAnimalClass;
  A: TAnimal;
  Classes: array[0..2] of TAnimalClass;
  I: Integer;
begin
  C := TDog;
  A := C.Create;
  Writeln(A.Sound, ' ', A.ClassName, ' ', C.ClassName);
  Writeln(A.ClassType = TDog, ' ', A.ClassType = TAnimal, ' ', C = TDog, ' ', C <> TPuppy);
  A.Free;

  Classes[0] := TAnimal;
  Classes[1] := TDog;
  Classes[2] := TPuppy;
  for I := 0 to 2 do
  begin
    A := Make(Classes[I]);
    Writeln(A.ClassName, ': ', A.Sound, ' ', A.InheritsFrom(TDog), ' ', Classes[I].InheritsFrom(TAnimal));
    A.Free;
  end;

  Writeln(TPuppy.ClassParent.ClassName, ' ', TPuppy.ClassParent.ClassParent.ClassName);
  Writeln(TAnimal.ClassParent.ClassName);
  Writeln(TDog.InstanceSize, ' ', TObject.InstanceSize);
  C := nil;
  Writeln(C = nil);
  A := TAnimal.Create;
  Writeln(A.ClassName);
  A.Free;
end.
