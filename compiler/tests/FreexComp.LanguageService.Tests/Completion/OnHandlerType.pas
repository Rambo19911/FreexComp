{ context: on-handler }
program OnHandlerType;
uses SysUtils;
type
  ENetwork = class(Exception);
begin
  try
    Writeln('x');
  except
    on E: {|} do
      Writeln('failed');
  end;
end.
{#has ENetwork, Exception, EConvertError}
{#top ENetwork}
{#not Writeln, Integer}
