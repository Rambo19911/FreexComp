unit Pong;

interface

const
  PongStep = 10;

function PongCount(N: Integer): Integer;

implementation

uses Ping;

function PongCount(N: Integer): Integer;
begin
  if N = 0 then
    Result := 0
  else
    Result := PongStep + PingCount(N - 1);
end;

initialization
  Writeln('init Pong ', PingCount(2));
finalization
  Writeln('final Pong');
end.
