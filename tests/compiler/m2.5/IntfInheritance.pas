// covers: LANG-660, LANG-665, ABI-091, ABI-095
program IntfInheritance;

{ A descendant interface: its IMT starts with the ancestor's methods. A class implementing the descendant converts to
  the ancestor interface implicitly (same hidden field), a descendant interface value to the ancestor (same pointer),
  and an ancestor parameter accepts both. A class inherits an interface from its parent class. }

type
  IAnimal = interface
    ['{5A5A5A5A-0000-1111-2222-333344445555}']
    function Name: string;
  end;

  IDog = interface(IAnimal)
    ['{5A5A5A5A-6666-7777-8888-9999AAAABBBB}']
    procedure Bark;
  end;

  TDog = class(TInterfacedObject, IDog)
    destructor Destroy; override;
    function Name: string;
    procedure Bark;
  end;

  TPuppy = class(TDog)
    function Name: string;
  end;

destructor TDog.Destroy;
begin
  Writeln('free ', Name);
  inherited Destroy;
end;

function TDog.Name: string;
begin
  Result := 'dog';
end;

procedure TDog.Bark;
begin
  Writeln(Name, ': woof');
end;

function TPuppy.Name: string;
begin
  Result := 'puppy';
end;

procedure Show(const A: IAnimal);
begin
  Writeln('animal ', A.Name);
end;

var
  Dog: IDog;
  Animal: IAnimal;
  Puppy: TPuppy;
begin
  Dog := TDog.Create;
  Dog.Bark;
  Animal := Dog;
  Writeln(Animal.Name, ' ', Pointer(Animal) = Pointer(Dog));
  Show(Dog);
  Dog := nil;
  Show(Animal);
  Animal := nil;
  Puppy := TPuppy.Create;
  Animal := Puppy;
  Show(Animal);
  Show(Puppy);
  Dog := Puppy;
  Dog.Bark;
  Writeln(Pointer(Animal) = Pointer(Dog));
  Animal := nil;
  Dog := nil;
  Writeln('done');
end.
