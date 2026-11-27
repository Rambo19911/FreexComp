unit Base;
interface
type
  TBase = class
    procedure Greet;
  end;
implementation
procedure TBase.{#}Greet;
begin
  Writeln('hello');
end;
end.
