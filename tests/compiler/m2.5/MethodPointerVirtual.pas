// covers: LANG-481, CMP-378
program MethodPointerVirtual;

{ A method pointer to a virtual method takes the implementation of the object's run-time class when it is created
  (the VMT slot is read then); later overrides of other objects do not matter. Class methods: the class reference's. }

type
  TShow = procedure of object;
  TName = function: string of object;

  TAnimal = class
    procedure Show; virtual;
    class function Kind: string; virtual;
  end;

  TDog = class(TAnimal)
    procedure Show; override;
    class function Kind: string; override;
  end;

  TAnimalClass = class of TAnimal;

procedure TAnimal.Show;
begin
  Writeln('animal');
end;

class function TAnimal.Kind: string;
begin
  Result := 'animal kind';
end;

procedure TDog.Show;
begin
  Writeln('dog');
end;

class function TDog.Kind: string;
begin
  Result := 'dog kind';
end;

var
  A: TAnimal;
  S: TShow;
  N: TName;
  C: TAnimalClass;
begin
  A := TDog.Create;
  S := A.Show;
  S;
  A.Free;
  A := TAnimal.Create;
  S := A.Show;
  S;
  C := TDog;
  N := C.Kind;
  Writeln(N);
  N := A.Kind;
  Writeln(N);
  A.Free;
end.
