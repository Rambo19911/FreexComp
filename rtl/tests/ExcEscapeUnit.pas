{ Shared unit of system/Exc_Escapes: with variant 2 its initialization raises, with variant 3 its finalization raises
  (the variant is ParamStr(1)). Uses only System. }
unit ExcEscapeUnit;

interface

type
  EEscape = class
  end;

implementation

initialization
  Writeln('init ExcEscapeUnit');
  if ParamStr(1) = '2' then
    raise EEscape.Create;
  Writeln('init ExcEscapeUnit done');

finalization
  Writeln('final ExcEscapeUnit');
  if ParamStr(1) = '3' then
    raise EEscape.Create;
  Writeln('final ExcEscapeUnit done');
end.
