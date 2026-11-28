unit UUse;

interface

procedure Show;

implementation

uses Store;

procedure Show;
begin
  Writeln('show ', Store.Twice);
end;

end.
