unit IndentContinuation;
interface
type
  TList<T> = class
  end;
  TMap = TList<TList<Integer>>;
  TRecord = record
    [Weak]
    FOwner: TObject;
  end;

implementation
procedure Show(A, B, C: Integer);
begin
end;

procedure Run;
var
  Total: Integer;
begin
  Show(1,
    2,
    3);
  Total := 1 +
    2 +
    3;
  if (Total > 1) and
    (Total < 10) then
    Show(Total, Total,
      Total);
end;
end.
