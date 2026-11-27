{ context: with }
program WithTwoTargetsHidden;
type
  TPerson = class
    Name: string;
    Age: Integer;
  end;
  TPet = class
  private
    FName: string;
  public
    property Name: string read FName;
    Species: string;
  end;
var
  Owner: TPerson;
  Pet: TPet;
begin
  with Owner, Pet do
    Writeln({|});
end.
{#has Name, Age, Species}
{#kind Name=Property, Age=Field}
