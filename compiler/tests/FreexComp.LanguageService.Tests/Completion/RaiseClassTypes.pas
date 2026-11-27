{ context: raise }
program RaiseClassTypes;
uses SysUtils;
type
  EParseError = class(Exception);
  TThing = class end;
begin
  raise {|}
end.
{#has EParseError, Exception, EConvertError, TThing, TObject}
{#top EParseError}
{#not Integer, Writeln}
