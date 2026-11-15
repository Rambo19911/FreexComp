// covers: LANG-705
program GenericLessThanAmbiguity;

type
  TBox<T> = class
    class function Make(const V: T): T;
  end;

class function TBox<T>.Make(const V: T): T;
begin
  Result := V;
end;

var
  A, B, C: Integer;
  F: Boolean;
begin
  A := 1;
  B := 2;
  C := 3;
  F := A < B;
  Writeln(F);
  F := (A < B) and (B > C);
  Writeln(F);
  Writeln(TBox<Integer>.Make(A) < B);
  if A < B then
    Writeln('less');
  Writeln(A < B, ' ', B > A);
  Writeln(TBox<Integer>.Make(C) > TBox<Integer>.Make(B));
end.