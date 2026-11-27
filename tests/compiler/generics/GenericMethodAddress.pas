// covers: LANG-981
program GenericMethodAddress;

type
  TIntFunc = function(X: Integer): Integer;
  TIntMethod = function(X: Integer): Integer of object;
  TIntRef = reference to function(X: Integer): Integer;
  TByteFunc = function(X: Byte): Byte;

  TUtil = class
    class function Twice<T>(X: T): T; static;
    class function Mix(X: Integer): Integer; overload; static;
    class function Mix<T>(X: T): T; overload; static;
    class procedure Run; static;
  end;

  TScaler = class
    Factor: Integer;
    function Scale<T>(X: T): T;
    class function Named<T>(X: T): T;
    procedure Run;
  end;

class function TUtil.Twice<T>(X: T): T;
begin
  Write('Twice<', SizeOf(T), '> ');
  Result := X;
end;

class function TUtil.Mix(X: Integer): Integer;
begin
  Write('Mix ');
  Result := X;
end;

class function TUtil.Mix<T>(X: T): T;
begin
  Write('Mix<', SizeOf(T), '> ');
  Result := X;
end;

function TScaler.Scale<T>(X: T): T;
begin
  Write('Scale<', SizeOf(T), '> by ', Factor, ' ');
  Result := X;
end;

class function TScaler.Named<T>(X: T): T;
begin
  Write(ClassName, '.Named<', SizeOf(T), '> ');
  Result := X;
end;

function Apply(const F: TIntRef; X: Integer): Integer;
begin
  Result := F(X);
end;

class procedure TUtil.Run;
var
  F: TIntFunc;
begin
  // unqualified, inside a method of the class
  F := Twice<Integer>;
  WriteLn(F(31));
end;

procedure TScaler.Run;
var
  M: TIntMethod;
  R: TIntRef;
begin
  M := Scale<Integer>;
  WriteLn(M(32));
  R := Scale<Integer>;
  WriteLn(R(33));
end;

var
  F, G: TIntFunc;
  S: TByteFunc;
  M: TIntMethod;
  R: TIntRef;
  Obj: TScaler;
begin
  // a static method: a plain procedural value
  F := TUtil.Twice<Integer>;
  WriteLn(F(21));
  S := TUtil.Twice<Byte>;
  WriteLn(S(200));

  // a method of an instance: a method pointer
  Obj := TScaler.Create;
  Obj.Factor := 3;
  M := Obj.Scale<Integer>;
  WriteLn(M(5));
  Obj.Factor := 4;
  WriteLn(M(6));

  // a class method: a method pointer whose data is the class
  M := TScaler.Named<Integer>;
  WriteLn(M(7));

  // a method reference, assigned and passed
  R := TUtil.Twice<Integer>;
  WriteLn(R(4));
  WriteLn(Apply(TUtil.Twice<Integer>, 8));
  R := Obj.Scale<Integer>;
  WriteLn(R(9));
  WriteLn(Apply(Obj.Scale<Integer>, 10));

  // the operand of @: one instance, one address
  WriteLn(@TUtil.Twice<Integer> = Pointer(F));
  WriteLn(@TUtil.Twice<Byte> = Pointer(F));
  G := @TUtil.Twice<Integer>;
  WriteLn(G(11), ' ', Pointer(G) = Pointer(F));

  // without type arguments the name is the non-generic method
  F := TUtil.Mix;
  WriteLn(F(12));
  WriteLn(@TUtil.Mix = Pointer(F));
  F := TUtil.Mix<Integer>;
  WriteLn(F(13));
  WriteLn(@TUtil.Mix = Pointer(F));

  TUtil.Run;
  Obj.Run;
  Obj.Free;
end.
