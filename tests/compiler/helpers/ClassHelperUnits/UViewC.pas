unit UViewC;

interface

uses
  UShape, UHelperB, UHelperC;

function ViewC(S: TShape): string;

implementation

function ViewC(S: TShape): string;
begin
  Result := S.Tag + ' ' + S.OnlyA + ' ' + S.OnlyC;
end;

end.