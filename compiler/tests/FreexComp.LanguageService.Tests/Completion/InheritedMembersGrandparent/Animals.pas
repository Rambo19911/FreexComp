unit Animals;
interface
type
  TAnimal = class
  private
    FLegs: Integer;
  public
    procedure Breathe;
    property Legs: Integer read FLegs;
  end;
  TDog = class(TAnimal)
  public
    procedure Bark;
  end;
implementation
procedure TAnimal.Breathe; begin end;
procedure TDog.Bark; begin end;
end.
