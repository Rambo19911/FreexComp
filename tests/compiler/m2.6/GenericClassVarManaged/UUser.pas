unit UUser;

interface

function UserCount: Integer;

implementation

uses
  UGen;

function UserCount: Integer;
begin
  Result := TRegistry<string>.Count;
end;

initialization
  TRegistry<string>.Title := Copy('strings!', 1, 7);
  TRegistry<string>.Add(Copy('user!', 1, 4));
finalization
  Writeln('user final ', TRegistry<string>.Count);
end.
