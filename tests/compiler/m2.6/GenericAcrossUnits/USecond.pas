unit USecond;

interface

function SecondSum: Integer;
function SecondClass: TClass;
function StringJoin: string;

implementation

uses
  UGenList;

function SecondSum: Integer;
var
  L: TCounted<Integer>;
begin
  L := TCounted<Integer>.Create;
  L.Add(10);
  Result := L.Get(0) + L.Count;
  L.Free;
end;

function SecondClass: TClass;
begin
  Result := TCounted<Integer>;
end;

function StringJoin: string;
var
  L: TCounted<string>;
begin
  L := TCounted<string>.Create;
  L.Add('x');
  L.Add('y');
  Result := L.Get(0) + L.Get(1);
  L.Free;
end;

end.
