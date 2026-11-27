{ context: unit-qualifier }
program UnitQualifierSysUtils;
uses SysUtils;
begin
  Writeln(SysUtils.{|});
end.
{#has IntToStr, Exception, FreeAndNil, Trim}
{#kind Exception=Class}
{#not Writeln, Halt}
