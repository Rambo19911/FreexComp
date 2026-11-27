unit Base;
interface
type
  TBase = class
    procedure Step; virtual;
  end;
implementation
procedure TBase.{#}Step;
begin
  Writeln('base');
end;
end.
