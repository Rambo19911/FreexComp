// covers: LANG-685, LANG-689
program ErrSafecall;

type
  TSafeProc = procedure(N: Integer); safecall;
  TPlainProc = procedure(N: Integer); stdcall;

  IValue = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8B30}']
    function Value: Integer; safecall;
  end;

  TBase = class(TInterfacedObject)
    constructor Create; safecall;
    destructor Destroy; override; safecall;
    function Compute: Integer; virtual; safecall;
  end;

  TDerived = class(TBase)
    function Compute: Integer; override;
  end;

  TImpl = class(TInterfacedObject, IValue)
    function Value: Integer;
  end;

  TVec = record
    X: Integer;
    class operator Add(const A, B: TVec): TVec; safecall;
  end;

procedure Later(N: Integer); safecall; forward;

procedure Printf(Format: PAnsiChar); cdecl; varargs; safecall; external 'msvcrt.dll' name 'printf';

constructor TBase.Create;
begin
end;

destructor TBase.Destroy;
begin
  inherited Destroy;
end;

function TBase.Compute: Integer;
begin
  Result := 1;
end;

function TDerived.Compute: Integer;
begin
  Result := 2;
end;

function TImpl.Value: Integer;
begin
  Result := 3;
end;

class operator TVec.Add(const A, B: TVec): TVec;
begin
  Result.X := A.X + B.X;
end;

procedure Later(N: Integer); stdcall;
begin
end;

procedure Plain(N: Integer);
begin
end;

procedure Outer;
  procedure Inner; safecall;
  begin
  end;
begin
  Inner;
end;

var
  S: TSafeProc;
  P: TPlainProc;
begin
  S := Plain;
  P := Later;
  S := P;
  P := S;
  Outer;
end.
