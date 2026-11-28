unit UUse;

interface

procedure Show;

implementation

uses UData;

procedure Show;
begin
  Writeln('show ', UData.Twice);
end;

end.
