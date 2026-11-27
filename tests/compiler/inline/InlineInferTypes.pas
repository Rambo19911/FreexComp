// covers: LANG-573
program InlineInferTypes;

{ var X := e takes the static type of any other expression: a class instance, a generic instance, an enumeration value,
  a set variable and a method-reference variable keep their types; a subrange-typed value declares a variable of the
  subrange's base type (an integer subrange its computation type Integer, a character subrange Char), so it can leave
  the subrange. }

type
  TColor = (Red, Green, Blue);
  TDigit = 0..9;
  TLower = 'a'..'z';
  TColors = set of TColor;
  TTransform = reference to function(A: Integer): Integer;

  TAnimal = class
  end;

  TBox<T> = class
  public
    Value: T;
    constructor Create(const AValue: T);
  end;

constructor TBox<T>.Create(const AValue: T);
begin
  inherited Create;
  Value := AValue;
end;

procedure Run;
var
  D: TDigit;
  L: TLower;
  Colors: TColors;
  Twice: TTransform;
begin
  var Animal := TAnimal.Create;
  Writeln(Animal.ClassName);
  Animal.Free;

  var Box := TBox<Integer>.Create(41);
  Box.Value := Box.Value + 1;
  Writeln(Box.ClassName, ' ', Box.Value);
  Box.Free;

  var Color := Blue;
  Writeln(Ord(Color), ' ', SizeOf(Color));
  Color := Green;
  Writeln(Ord(Color));

  D := 9;
  var N := D;
  Writeln(SizeOf(D), ' ', SizeOf(N));
  N := N + 91;
  Writeln(N);

  L := 'z';
  var C := L;
  Writeln(SizeOf(C));
  C := 'A';
  Writeln(C, ' ', Ord(C));

  Colors := [Red, Blue];
  var Copy := Colors;
  Include(Copy, Green);
  Writeln(SizeOf(Copy), ' ', Green in Copy, ' ', Green in Colors);

  Twice := function(A: Integer): Integer
    begin
      Result := A * 2;
    end;
  var F := Twice;
  Writeln(F(21));
end;

begin
  Run;
end.
