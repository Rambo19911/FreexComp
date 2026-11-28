program RecordHelperLiteral;
type
  TIntHelper = record helper for Integer
    function Squared: Integer;
  end;
function TIntHelper.{#}Squared: Integer;
begin
  Result := Self * Self;
end;
begin
  Writeln(7.{|}Squared);
end.
