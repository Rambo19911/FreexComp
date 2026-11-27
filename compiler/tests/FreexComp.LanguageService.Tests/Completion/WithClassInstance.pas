{ context: with }
program WithClassInstance;
type
  TLogger = class
    procedure Info(const S: string);
    procedure Warn(const S: string);
  end;
procedure TLogger.Info(const S: string); begin end;
procedure TLogger.Warn(const S: string); begin end;
begin
  with TLogger.Create do
  try
    {|}
  finally
    Free;
  end;
end.
{#has Info, Warn, Free, ClassName}
{#kind Info=Method}
