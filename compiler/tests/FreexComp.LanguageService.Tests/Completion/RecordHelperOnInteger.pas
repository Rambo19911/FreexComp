{ context: record-helper }
program RecordHelperOnInteger;
type
  TIntegerHelper = record helper for Integer
    function IsEven: Boolean;
    function Twice: Integer;
  end;
function TIntegerHelper.IsEven: Boolean; begin Result := not Odd(Self) end;
function TIntegerHelper.Twice: Integer; begin Result := Self * 2 end;
var
  N: Integer;
begin
  if N.{|}
end.
{#has IsEven, Twice}
{#not N}
