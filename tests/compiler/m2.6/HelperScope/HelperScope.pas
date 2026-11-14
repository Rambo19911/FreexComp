// covers: LANG-753
program HelperScope;

uses
  UHelperA, UHelperB;

function BeforeOwnHelper(I: Integer): string;
begin
  Result := I.Name;
end;

type
  TIntHelperC = record helper for Integer
    function Name: string;
  end;

function TIntHelperC.Name: string;
begin
  Result := 'C';
end;

var
  I: Integer;
begin
  I := 1;
  Writeln(FromA(I), ' ', FromB(I), ' ', BeforeOwnHelper(I), ' ', I.Name);
end.