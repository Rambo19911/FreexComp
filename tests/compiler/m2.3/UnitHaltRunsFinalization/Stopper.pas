unit Stopper;

interface

procedure Stop(Code: Integer);

implementation

var
  Reason: string;

procedure Stop(Code: Integer);
begin
  Reason := 'halted';
  Reason := Reason + ' with code';
  Writeln('stopping');
  Halt(Code);
end;

initialization
  Writeln('init Stopper');
finalization
  Writeln('final Stopper: ', Reason);
end.