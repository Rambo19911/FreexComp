// covers: LANG-600, LANG-604, LANG-615, LANG-620, LANG-621, LANG-630, LANG-634
program ClassBasic;

type
  TCounter = class
    FCount: Integer;
    FName: string;
    procedure Increment;
    procedure Add(N: Integer);
    function Value: Integer;
    procedure Show;
  end;

procedure TCounter.Increment;
begin
  FCount := FCount + 1;
end;

procedure TCounter.Add(N: Integer);
var
  I: Integer;
begin
  for I := 1 to N do
    Increment;
end;

function TCounter.Value: Integer;
begin
  Result := FCount;
end;

procedure TCounter.Show;
begin
  Writeln(FName, '=', Value);
end;

var
  C, D: TCounter;
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  C := TCounter.Create;
  Writeln(C.FCount, ' ', C.FName = '');
  C.Increment;
  C.Add(4);
  Writeln(C.Value);
  C.FName := 'c';
  C.Show;
  D := TCounter.Create;
  D.FName := 'd';
  D.Add(2);
  D.Show;
  Writeln(C <> D, ' ', C = C);
  D.Free;
  C.Free;
  C := nil;
  Writeln(C = nil);
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
