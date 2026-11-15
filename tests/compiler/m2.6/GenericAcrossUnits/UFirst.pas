unit UFirst;

interface

uses
  UGenList;

function FirstSum: Integer;
function FirstClass: TClass;

implementation

function FirstSum: Integer;
var
  L: TCounted<Integer>;
begin
  L := TCounted<Integer>.Create;
  L.Add(1);
  L.Add(2);
  Result := L.Get(0) + L.Get(1);
  L.Free;
end;

function FirstClass: TClass;
begin
  Result := TCounted<Integer>;
end;

end.
