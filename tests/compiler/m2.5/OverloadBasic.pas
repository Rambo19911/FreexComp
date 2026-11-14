// covers: LANG-440, LANG-441, LANG-442
program OverloadBasic;

procedure Show(I: Integer); overload;
begin
  Writeln('Integer ', I);
end;

procedure Show(const S: string); overload;
begin
  Writeln('string ', S);
end;

procedure Show(I, J: Integer); overload;
begin
  Writeln('two ', I, ' ', J);
end;

procedure Show; overload;
begin
  Writeln('none');
end;

procedure Show(B: Boolean); overload;
begin
  Writeln('Boolean ', B);
end;

function Twice(I: Integer): Integer; overload;
begin
  Result := I * 2;
end;

function Twice(const S: string): string; overload;
begin
  Result := S + S;
end;

procedure Outer;

  procedure Show(C: Char); overload;
  begin
    Writeln('nested Char ', C);
  end;

begin
  Show('x');
  Show(7);
end;

var
  N: Integer;
  T: string;
begin
  Show(1);
  Show('abc');
  Show(1, 2);
  Show;
  Show(True);
  N := 21;
  T := 'ab';
  Show(N);
  Show(T);
  Writeln(Twice(N));
  Writeln(Twice(T));
  Show(Twice(3));
  Show(Twice('-'));
  Outer;
end.
