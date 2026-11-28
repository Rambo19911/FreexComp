{ context: parameters }
program AnonymousNested;
type
  TProc = reference to procedure;
  TIntFunc = reference to function(Step: Integer): Integer;
procedure Work(Seed: Integer);
var
  Outer: TProc;
begin
  Outer := procedure
    var Middle: Integer;
      Inner: TIntFunc;
    begin
      Middle := Seed;
      Inner := function(Step: Integer): Integer
        var Deep: Integer;
        begin
          Deep := Step;
          {|}
          Result := Deep + Middle + Seed;
        end;
      Middle := Inner(1);
    end;
  Outer();
end;
begin
end.
{#has Deep, Step, Result, Middle, Inner, Seed, Outer}
{#kind Step=Parameter, Seed=Parameter, Deep=Variable, Middle=Variable}
{#top Deep}
