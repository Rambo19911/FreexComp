{ context: expected-type }
program ExpectedDescendantClass;
type
  TAnimal = class end;
  TDog = class(TAnimal) end;
  TStone = class end;
var
  Animal: TAnimal;
  Dog: TDog;
  Stone: TStone;
  Thing: TObject;
function NewDog: TDog;
begin
  Result := nil;
end;
begin
  Animal := {|}
end.
{#ranked Animal, Dog, NewDog, nil}
{#unranked Stone, Thing, TDog}
