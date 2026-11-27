{ context: with }
program WithNested;
type
  TOuter = record
    Shared: Integer;
    OuterOnly: Integer;
  end;
  TInner = class
    InnerOnly: Integer;
    procedure Go;
    function Shared: string;
  end;
procedure TInner.Go; begin end;
function TInner.Shared: string; begin Result := '' end;
var
  O: TOuter;
  I: TInner;
begin
  with O do
    with I do
    begin
      {|}
    end;
end.
{#has Shared, OuterOnly, InnerOnly, Go}
{#kind Shared=Method, OuterOnly=Field}
