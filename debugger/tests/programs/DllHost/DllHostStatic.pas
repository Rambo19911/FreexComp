program DllHostStatic;
// console host that imports the test library at load time

function Scale(Value, Factor: Integer): Integer; stdcall; external 'DllLib.dll';

begin
  WriteLn('host start');
  WriteLn('scaled ', Scale(6, 7));
  WriteLn('host end');
end.
