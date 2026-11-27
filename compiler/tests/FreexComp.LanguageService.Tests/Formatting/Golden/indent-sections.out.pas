unit IndentSections;
interface
uses
  SysUtils,
  Classes;
const
  Max = 10;
  Names: array[0..1] of string = ('a',
    'b');
resourcestring
  SHello = 'Hello';
type
  TIndex = 0..Max;
var
  Count: Integer;

implementation
threadvar
  Local: Integer;
procedure Run;
label
  L1;
var
  X: Integer;
begin
  goto L1;
  L1:
  X := 1;
end;
end.
