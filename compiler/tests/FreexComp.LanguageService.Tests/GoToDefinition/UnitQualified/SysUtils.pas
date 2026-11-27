unit SysUtils;
interface
function IntToStr(Value: Integer): string;
implementation
function {#}IntToStr(Value: Integer): string;
begin
  Str(Value, Result);
end;
end.
