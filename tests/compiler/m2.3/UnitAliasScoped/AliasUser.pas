unit AliasUser;

interface

uses Types;

function ViaCanonical: Integer;

implementation

function ViaCanonical: Integer;
begin
  Result := Types.Twice(10) + InitCount;
end;

initialization
  Writeln('init AliasUser');
end.
