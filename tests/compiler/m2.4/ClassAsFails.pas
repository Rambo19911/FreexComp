// covers: LANG-651
program ClassAsFails;

{ 'as' to a class the object's run-time class does not descend from: runtime error 219 (without SysUtils). }

type
  TA = class
  end;

  TB = class(TA)
  end;

  TC = class(TA)
  end;

var
  A: TA;
  B: TB;
begin
  A := TC.Create;
  Writeln('before');
  B := A as TB;
  Writeln('not reached');
end.
