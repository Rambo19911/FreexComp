unit UViewAB;

interface

uses
  UShape;

function ViewAB(S: TShape): string;

implementation

uses
  UHelperB, UHelperA;

function ViewAB(S: TShape): string;
begin
  Result := S.Tag + ' ' + S.OnlyA;
end;

end.