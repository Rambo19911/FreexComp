// covers: CMP-1071, LANG-547, LANG-548, LANG-549, LANG-550
program usemath;

uses Windows;

type
  TVec3 = record X, Y, Z: Integer end;
  TAdd = function(A, B: Integer): Integer; stdcall;
  TMakeVec = function(X, Y, Z: Integer): TVec3; cdecl;

// (a) load-time linking against mathlib.lib, found through -L
function Add(A, B: Integer): Integer; stdcall; external 'mathlib.dll';
function Scale(V, Factor: Double): Double; cdecl; external 'mathlib.dll' name 'scale_value';
function MakeVec(X, Y, Z: Integer): TVec3; cdecl; external 'mathlib.dll' name 'make_vec';
function Greeting: PWideChar; external 'mathlib.dll';

var
  V: TVec3;
  H: HMODULE;
  DynAdd: TAdd;
  DynMakeVec: TMakeVec;
begin
  Writeln('Add(2, 3) = ', Add(2, 3));
  Writeln('Scale(1.5, 4) = ', Scale(1.5, 4.0):0:2);
  V := MakeVec(1, 2, 3);
  Writeln('MakeVec = ', V.X, ' ', V.Y, ' ', V.Z);
  Writeln('Greeting = ', string(Greeting));

  // (b) run-time linking: by name, by ordinal, and the wrong case
  H := LoadLibraryW('mathlib.dll');
  Writeln('loaded: ', H <> 0);
  DynAdd := TAdd(GetProcAddress(H, 'Add'));
  Writeln('by name: ', DynAdd(40, 2));
  DynMakeVec := TMakeVec(GetProcAddress(H, PAnsiChar(7)));
  V := DynMakeVec(4, 5, 6);
  Writeln('by ordinal 7: ', V.X, ' ', V.Y, ' ', V.Z);
  Writeln('wrong case is nil: ', GetProcAddress(H, 'add') = nil);
  Writeln('make_vec by name is ordinal 7: ', GetProcAddress(H, 'make_vec') = GetProcAddress(H, PAnsiChar(7)));
  Writeln('MakeVec is not exported: ', GetProcAddress(H, 'MakeVec') = nil);
  Writeln('freed: ', FreeLibrary(H));
end.
