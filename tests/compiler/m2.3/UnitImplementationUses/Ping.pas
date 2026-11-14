unit Ping;

interface

function PingCount(N: Integer): Integer;

implementation

uses Pong;

function PingCount(N: Integer): Integer;
begin
  if N = 0 then
    Result := 0
  else
    Result := 1 + PongCount(N - 1);
end;

initialization
  Writeln('init Ping ', Pong.PongStep);
finalization
  Writeln('final Ping');
end.
