{ context: member-method }
program MemberConstructorResult;
type
  TBase = class
    procedure Run;
  end;
  TJob = class(TBase)
    procedure Cancel;
  end;
procedure TBase.Run; begin end;
procedure TJob.Cancel; begin end;
begin
  TJob.Create.{|}
end.
{#has Cancel, Run, Free}
{#kind Cancel=Method}
{#not TJob, TBase}
