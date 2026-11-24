program DllHost;
// console host of the test library: load, call, free

uses Windows;

type
  TScale = function(Value, Factor: Integer): Integer; stdcall;
  TGuarded = function(Value: Integer): Integer; stdcall;

var
  H: HMODULE;
  Scale: TScale;
  Guarded: TGuarded;
  R: Integer;
begin
  WriteLn('host start');
  H := LoadLibraryW('DllLib.dll');
  WriteLn('loaded ', H <> 0);
  Scale := TScale(GetProcAddress(H, 'Scale'));
  Guarded := TGuarded(GetProcAddress(H, 'Guarded'));
  R := Scale(6, 7);
  WriteLn('scaled ', R);
  R := Guarded(5);
  WriteLn('guarded ', R);
  WriteLn('freed ', FreeLibrary(H));
  WriteLn('host end');
end.
