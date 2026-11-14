unit UHelperB;

interface

type
  TIntHelperB = record helper for Integer
    function Name: string;
  end;

function FromB(I: Integer): string;

implementation

function TIntHelperB.Name: string;
begin
  Result := 'B';
end;

function FromB(I: Integer): string;
begin
  Result := I.Name;
end;

end.