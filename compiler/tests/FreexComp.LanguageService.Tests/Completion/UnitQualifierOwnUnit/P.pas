{ context: unit-qualifier }
program P;
uses Geometry;
begin
  Writeln(Geometry.{|});
end.
{#has TCircle, Pi2, CircleArea}
{#kind TCircle=Class, Pi2=Constant, CircleArea=Routine}
{#not Helper, Writeln}
