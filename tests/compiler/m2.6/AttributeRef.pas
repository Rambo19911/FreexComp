// covers: LANG-763
program AttributeRef;

type
  TPair = record
    A, B: Integer;
  end;

  TBox = class
    procedure Put([Ref] const X: Integer);
  end;

var
  G: Integer;
  P: TPair;
  S: string;
  B: TBox;

procedure ShowInt([Ref] const X: Integer);
begin
  G := G + 1;
  Writeln(X, ' ', @X = @G);
end;

procedure ShowValue(const X: Integer);
begin
  G := G + 1;
  Writeln(X, ' ', @X = @G);
end;

procedure ShowPair([Ref] const R: TPair);
begin
  P.A := P.A + 10;
  Writeln(R.A, ' ', R.B);
end;

procedure ShowString([Ref] const T: string);
begin
  Writeln(T, ' ', @T = @S);
end;

function Twice([Ref] const X: Integer): Integer;
begin
  Result := X * 2;
end;

procedure TBox.Put([Ref] const X: Integer);
begin
  G := 100;
  Writeln('box ', X);
end;

begin
  G := 1;
  ShowInt(G);
  ShowValue(G);
  ShowInt(7);
  P.A := 1;
  P.B := 2;
  ShowPair(P);
  S := 'abc';
  ShowString(S);
  Writeln(Twice(G + 1));
  B := TBox.Create;
  G := 5;
  B.Put(G);
  B.Free;
end.
