// covers: LANG-710, LANG-716, CMP-162, CMP-385
program GenericAcrossUnits;

uses
  UGenList, UFirst, USecond;

var
  L: TCounted<Integer>;
begin
  L := TCounted<Integer>.Create;
  L.Add(5);
  Writeln(L.Count, ' ', L.Total, ' ', TCounted<Integer>.Instances);
  L.Free;
  Writeln(FirstSum, ' ', SecondSum, ' ', TCounted<Integer>.Instances);
  Writeln(FirstClass = SecondClass, ' ', FirstClass = TCounted<Integer>, ' ', Calls);
  Writeln(TCounted<string>.Instances, ' ', StringJoin);
end.
