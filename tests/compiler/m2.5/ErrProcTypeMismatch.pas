// covers: LANG-480, LANG-483
program ErrProcTypeMismatch;

{ LANG-483: a routine with another parameter type, another modifier or another result type is not compatible (E2010);
  an instance method is no plain procedural value (E2010); no overload fits (E2131). }

type
  TIntFunc = function(X: Integer): Integer;
  TProc = procedure(var X: Integer);

  TFoo = class
    function Inst(X: Integer): Integer;
  end;

function TFoo.Inst(X: Integer): Integer;
begin
  Result := X;
end;

function ByteArg(X: Byte): Integer;
begin
  Result := X;
end;

procedure ValueArg(X: Integer);
begin
end;

function Over(const S: string): Integer; overload;
begin
  Result := 0;
end;

function Over(X: Int64): Integer; overload;
begin
  Result := 0;
end;

var
  F: TIntFunc;
  P: TProc;
begin
  F := ByteArg;
  P := ValueArg;
  F := TFoo.Inst;
  F := Over;
end.
