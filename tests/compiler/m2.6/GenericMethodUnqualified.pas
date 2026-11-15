// covers: LANG-701, LANG-714
program GenericMethodUnqualified;
type
  TUtil = class
    class function Max<T>(const A, B: T): T;
    class function Both: Integer;
    function Inst: string;
    function Echo<T>(const X: T): T;
  end;
  TRec = record
    class function Pick<T>(const A, B: T; First: Boolean): T; static;
    class function Use: Integer; static;
  end;
class function TUtil.Max<T>(const A, B: T): T;
begin
  if A > B then Result := A else Result := B;
end;
class function TUtil.Both: Integer;
begin
  Result := Max<Integer>(3, 4) + Max(10, 2);
end;
function TUtil.Inst: string;
begin
  Result := Max<string>('a', 'b') + Max('d', 'c') + Echo('z') + Echo<string>('y');
end;
function TUtil.Echo<T>(const X: T): T;
begin
  Result := X;
end;
class function TRec.Pick<T>(const A, B: T; First: Boolean): T;
begin
  if First then Result := A else Result := B;
end;
class function TRec.Use: Integer;
begin
  Result := Pick<Integer>(1, 2, False) + Pick(5, 6, True);
end;
var U: TUtil;
begin
  Writeln(TUtil.Both);
  U := TUtil.Create;
  Writeln(U.Inst);
  U.Free;
  Writeln(TRec.Use);
end.
