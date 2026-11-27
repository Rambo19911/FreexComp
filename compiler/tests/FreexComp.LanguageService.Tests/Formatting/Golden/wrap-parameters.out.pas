program WrapParameters;

procedure Configure(const Name: string; Width,
  Height: Integer; Visible: Boolean; var Count: Integer);
begin
end;

var
  Alpha, Bravo, Charlie, Delta, Echo, Foxtrot, Golf,
    Hotel: Integer;
  Values: array[0..4] of Integer = (1000, 2000, 3000, 4000,
    5000);
begin
  Configure('first', Alpha, Bravo, True, Charlie);
end.
