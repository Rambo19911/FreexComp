unit UHelperA;

interface

type
  TIntHelperA = record helper for Integer
    function Name: string;
  end;

function FromA(I: Integer): string;

implementation

function TIntHelperA.Name: string;
begin
  Result := 'A';
end;

function FromA(I: Integer): string;
begin
  Result := I.Name;
end;

end.