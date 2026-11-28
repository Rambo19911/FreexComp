program P;

{ CMP-1511: a field; a local of the same name in another routine keeps its name. }

type
  TCounter = class
    Amount: Integer;
    procedure Bump;
  end;

procedure TCounter.Bump;
begin
  Amount := Amount + 1;
end;

procedure Show;
var
  Value: Integer;
begin
  Value := 10;
  Writeln('local ', Value);
end;

var
  C: TCounter;
begin
  C := TCounter.Create;
  C.Bump;
  C.Bump;
  Writeln('field ', C.Amount);
  Show;
  C.Free;
end.
