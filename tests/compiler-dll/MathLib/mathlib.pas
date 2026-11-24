// covers: LANG-547, LANG-548, LANG-549, LANG-550, CMP-1053, CMP-1054
library mathlib;

type
  TVec3 = record X, Y, Z: Integer end;

function Add(A, B: Integer): Integer; stdcall;
begin
  Result := A + B;
end;

function Scale(V, Factor: Double): Double; cdecl;
begin
  Result := V * Factor;
end;

// 12 bytes: returned through the hidden result pointer, which the export thunk hands back
function MakeVec(X, Y, Z: Integer): TVec3; cdecl;
begin
  Result.X := X;
  Result.Y := Y;
  Result.Z := Z;
end;

function Greeting: PWideChar;
begin
  Result := 'hello from mathlib';
end;

exports
  Add,                                // ordinal 1
  Scale name 'scale_value',           // ordinal 2
  MakeVec name 'make_vec' index 7,
  Greeting;                           // ordinal 3

begin
end.
