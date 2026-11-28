{ context: record-helper }
program RecordHelperOnLiteral;
type
  TTextHelper = record helper for string
    function Shout: string;
    function Words: Integer;
  end;
function TTextHelper.Shout: string; begin Result := Self + '!' end;
function TTextHelper.Words: Integer; begin Result := 1 end;
var
  S: string;
begin
  S := 'abc'.{|}
end.
{#has Shout, Words}
{#kind Shout=Method}
