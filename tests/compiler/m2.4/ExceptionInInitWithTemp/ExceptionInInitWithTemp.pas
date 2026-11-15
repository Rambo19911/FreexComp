// covers: LANG-832, ABI-104, CMP-355
program ExceptionInInitWithTemp;

{ C-241: the main block holds a managed temporary (the string result of ParamStr); an exception escaping a unit
  initialization must still end the program with exit code 217, so the temporary's cleanup in the main routine must see
  a zeroed slot even though the main block never ran }

uses
  RaiseInit;

begin
  if ParamStr(1) = 'm' then
    Writeln;
end.
