{ context: parameters }
program AnonymousParameterHidesLocal;
type
  TIntFunc = reference to function(Value: Integer): Integer;
procedure Work;
var
  Count: Integer;
  Base: Integer;
  Pick: TIntFunc;
begin
  Count := 1;
  Base := 2;
  Pick := function(Count: Integer): Integer
    begin
      Result := Base + {|}
    end;
  Count := Pick(Count);
end;
begin
end.
{#has Count, Base, Result, Pick}
{#kind Count=Parameter, Base=Variable, Result=Variable}
{#not Value}
