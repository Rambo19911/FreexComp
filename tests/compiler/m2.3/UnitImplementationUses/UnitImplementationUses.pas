// covers: LANG-513, LANG-515, LANG-520, LANG-521
program UnitImplementationUses;
// Ping and Pong use each other in their implementation uses clauses (allowed, LANG-513) and call each other.
// Init order (LANG-520): Ping is visited first, its implementation uses reach Pong, whose own reference back to
// Ping is skipped (being visited) -> System, Pong, Ping.
uses Ping, Pong;
begin
  Writeln('main');
  Writeln(PingCount(5));
  Writeln(PongCount(4));
  Writeln(Pong.PongStep);
end.
