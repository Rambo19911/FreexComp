// covers: LANG-568
program ErrInlinePlacement;

{ An inline declaration must be an element of a statement list: as the statement of then, else, do or a case arm it is
  E2077. An initialiser after a list of several names is E2078, with and without a type. }

var
  Flag: Boolean;
  N: Integer;
begin
  Flag := True;
  N := 1;
  if Flag then
    var A := 1;
  if Flag then
    N := 2
  else
    var B: Integer;
  while Flag do
    var C := 3;
  for N := 1 to 2 do
    const D = 4;
  case N of
    1: var E := 5;
    2: const F = 6;
  else
    var Allowed := 7;
    N := Allowed;
  end;

  var G, H: Integer := 8;
  var I, J := 9;
  var K, L: Integer;
  K := 1;
  L := K;
  N := L;
end.
