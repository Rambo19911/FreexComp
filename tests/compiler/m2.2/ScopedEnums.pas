// covers: LANG-210
program ScopedEnums;
{$SCOPEDENUMS ON}
type
  TFruit = (Apple, Pear, Plum);
{$SCOPEDENUMS OFF}
type
  TShape = (Circle, Square);
const
  Apple = 42;
var
  F: TFruit;
  S: TShape;
begin
  F := TFruit.Pear;
  Writeln(Ord(F));
  Writeln(Apple);
  Writeln(Ord(TFruit.Apple), ' ', Ord(High(TFruit)));
  S := Square;
  Writeln(Ord(S), ' ', Ord(TShape.Circle));
  case F of
    TFruit.Apple: Writeln('apple');
    TFruit.Pear: Writeln('pear');
  end;
  if F < TFruit.Plum then
    Writeln('before plum');
end.
