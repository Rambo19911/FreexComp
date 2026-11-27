// covers: LANG-791, CMP-1397
program ClassDtorAfterFinalizationExit;

{ Exit in a unit's finalization section ends only its statements: the unit's class destructors still run, then its
  managed globals and the managed class vars of the instances it took the turn of are released (no leak at the probe) }

uses
  UFEProbe, UFExit;

begin
  Writeln('main');
  Fill;
end.
