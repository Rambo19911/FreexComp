// covers: LANG-545, CMP-1054
library ErrExportNotExportable;
// only a procedure or function declared at unit level, with a body, can be exported:
// one line per reason

type
  TVec3 = record
    X, Y, Z: Integer;
    class function Zero: TVec3; static;
  end;

const
  Limit = 10;

var
  Counter: Integer;

class function TVec3.Zero: TVec3;
begin
  Result.X := 0; Result.Y := 0; Result.Z := 0;
end;

procedure Show(A: Integer); overload;
begin
end;

procedure Show(A: Double); overload;
begin
end;

function Tick: Cardinal; stdcall; external 'kernel32.dll' name 'GetTickCount';

procedure Outer;
  procedure Inner;
  begin
  end;
begin
  Inner;
end;

// 12 bytes: the result travels through the hidden result pointer, which only an export thunk returns
function MakeVec(X, Y, Z: Integer): TVec3; public name 'make_vec';
begin
  Result.X := X; Result.Y := Y; Result.Z := Z;
end;

exports
  Counter,
  Limit,
  TVec3,
  System,
  TVec3.Zero,
  Outer.Inner,
  Show,
  SizeOf,
  Tick,
  MakeVec name 'make_vec',
  MakeVec name 'vec3';          // fine: another name gets a thunk

begin
end.
