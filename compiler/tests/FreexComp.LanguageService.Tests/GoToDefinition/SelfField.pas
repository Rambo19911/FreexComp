program SelfField;
type
  TCounter = class
    {#}FCount: Integer;
    procedure Bump;
  end;
procedure TCounter.Bump;
begin
  {|}FCount := FCount + 1;
end;
begin
  TCounter.Create.Bump;
end.
