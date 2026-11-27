{ context: generic-constraint }
program GenericConstraintInterface;
type
  INamed = interface
    function GetName: string;
    property Name: string read GetName;
  end;
  TPrinter<T: INamed> = class
    procedure Show(const Item: T);
  end;
procedure TPrinter<T>.Show(const Item: T);
begin
  Writeln(Item.{|});
end;
begin
end.
{#has GetName, Name}
{#kind Name=Property}
