// covers: LANG-700, LANG-711, LANG-484
program GenericProcTypeCall;

type
  TFn<T> = function(const A: T): Integer;
  TProc<T> = procedure(const A: T);

  TBox<T> = class
    F: TFn<T>;
    P: TProc<T>;
    function Call(const A: T): Integer;
    procedure Run(const A: T);
  end;

function TBox<T>.Call(const A: T): Integer;
var
  G: TFn<T>;
begin
  G := F;
  Result := F(A) + G(A);
end;

procedure TBox<T>.Run(const A: T);
begin
  P(A);
end;

function Twice(const A: Integer): Integer;
begin
  Result := A * 2;
end;

function Len(const A: string): Integer;
begin
  Result := Length(A);
end;

procedure Show(const A: string);
begin
  Writeln('show ', A);
end;

var
  I: TBox<Integer>;
  S: TBox<string>;
begin
  I := TBox<Integer>.Create;
  I.F := Twice;
  Writeln(I.Call(5));
  I.Free;
  S := TBox<string>.Create;
  S.F := Len;
  S.P := Show;
  Writeln(S.Call('abc'));
  S.Run('x');
  S.Free;
end.
