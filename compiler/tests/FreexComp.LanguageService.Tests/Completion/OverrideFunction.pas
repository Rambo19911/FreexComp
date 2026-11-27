{ context: override }
program OverrideFunction;
type
  TBase = class
    function Describe: string; virtual;
    function Weight(Factor: Integer): Integer; virtual; abstract;
  end;
  TDerived = class(TBase)
    function {|}
  end;
function TBase.Describe: string; begin Result := '' end;
begin
end.
{#has Describe, Weight}
{#not TBase}
