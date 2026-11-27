unit URegA;

{ the first module that defines TRegistry<Integer> and TRegistry<string>: their class constructors run here }

interface

uses
  UReg;

function CountA: Integer;

implementation

function CountA: Integer;
begin
  Result := TRegistry<Integer>.Count + TRegistry<string>.Count;
end;

initialization
  Writeln('URegA init');
finalization
  Writeln('URegA final');
end.
