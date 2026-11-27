unit URegB;

{ defines TRegistry<Integer> and TRegistry<string> again (skipped at run time) and TRegistry<Byte> first }

interface

uses
  UReg;

function CountB: Integer;

implementation

function CountB: Integer;
begin
  Result := TRegistry<Integer>.Count + TRegistry<string>.Count + TRegistry<Byte>.Count;
end;

initialization
  Writeln('URegB init');
finalization
  Writeln('URegB final');
end.
