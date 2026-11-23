program DbgDemo;

type
  TColor = (clRed, clGreen, clBlue);
  TColors = set of TColor;
  TPoint = record
    X, Y: Integer;
  end;
  TAnimal = class
    Name: string;
    Legs: Integer;
    constructor Create(const AName: string; ALegs: Integer);
    function Describe: string; virtual;
  end;
  TDog = class(TAnimal)
    function Describe: string; override;
  end;

constructor TAnimal.Create(const AName: string; ALegs: Integer);
begin
  Name := AName;
  Legs := ALegs;
end;

function TAnimal.Describe: string;
begin
  Result := Name;
end;

function TDog.Describe: string;
begin
  Result := 'Dog ' + Name;
end;

function Factorial(N: Integer): Integer;
begin
  if N <= 1 then
    Result := 1
  else
    Result := N * Factorial(N - 1);
end;

procedure Run;
var
  I, Total: Integer;
  Ch: Char;
  Ok: Boolean;
  Color: TColor;
  Colors: TColors;
  S: string;
  P: TPoint;
  Nums: array of Integer;
  Pet: TAnimal;
  PTotal: ^Integer;
begin
  Total := 0;
  Ch := 'A';
  Ok := True;
  Color := clGreen;
  Colors := [clRed, clBlue];
  S := 'Héllo';
  P.X := 3;
  P.Y := 4;
  SetLength(Nums, 3);
  for I := 0 to 2 do
    Nums[I] := (I + 1) * 10;
  Pet := TDog.Create('Rex', 4);
  PTotal := @Total;
  Total := Factorial(4);
  WriteLn(Pet.Describe);
  WriteLn(Total);
  Pet.Free;
  WriteLn('done');
end;

begin
  Run;
end.