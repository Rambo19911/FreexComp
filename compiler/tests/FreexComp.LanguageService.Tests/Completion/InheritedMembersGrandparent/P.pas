{ context: inherited-members }
program P;
uses Animals;
type
  TPuppy = class(TDog)
  end;
var
  Pup: TPuppy;
begin
  Pup.{|}
end.
{#has Bark, Breathe, Legs}
{#kind Legs=Property}
