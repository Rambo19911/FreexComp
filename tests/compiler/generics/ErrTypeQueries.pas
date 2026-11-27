// covers: LANG-972, LANG-977
program ErrTypeQueries;

type
  TCheck<T> = class
  public
    class function Kept(const V: T): Integer;
    class function Plain(const V: T): Integer;
  end;

// the kept branch of an instance is bound: for string that is the else part
class function TCheck<T>.Kept(const V: T): Integer;
begin
  if GetTypeKind(T) = tkInteger then
    Result := Integer(V)
  else
    Result := V + 1;
end;

// SizeOf(T) is a constant but no type query: both parts are bound for every instance
class function TCheck<T>.Plain(const V: T): Integer;
begin
  Result := 0;
  if SizeOf(T) = 4 then
    Result := Integer(V);
end;

// outside generics the statement that is not taken is still checked
procedure NotGeneric;
begin
  if IsManagedType(Integer) then
    Missing := 1
  else
    Writeln('plain');
end;

var
  V: Integer;
  B: Boolean;
  P: Pointer;
begin
  B := IsManagedType(V);
  B := IsConstValue;
  B := IsManagedType;
  P := @IsManagedType;
  P := Addr(GetTypeKind);
  V := TCheck<Integer>.Kept(1) + TCheck<string>.Kept('a');
  V := TCheck<string>.Plain('a');
  NotGeneric;
end.
