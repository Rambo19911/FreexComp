{ context: expected-type }
program ExpectedNilPointerArrayProcedural;
type
  TNumbers = array of Integer;
  TCallback = procedure(Value: Integer);
var
  Numbers: TNumbers;
  Callback: TCallback;
  P: PInteger;
  Alpha: Integer;
procedure Run(N: TNumbers; C: TCallback; Q: PInteger);
begin
end;
begin
  Run(nil, nil, {|});
end.
{#ranked nil, P}
{#unranked Alpha, Numbers, Callback, True}
