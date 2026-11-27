program PropertyNotAccessor;
type
  TBox = class
  private
    FSize: Integer;
    function GetSize: Integer;
    procedure SetSize(Value: Integer);
  public
    property {#}Size: Integer read GetSize write SetSize;
  end;
function TBox.GetSize: Integer;
begin
  Result := FSize;
end;
procedure TBox.SetSize(Value: Integer);
begin
  FSize := Value;
end;
var
  B: TBox;
begin
  B := TBox.Create;
  B.{|}Size := 4;
  Writeln(B.Size);
end.
