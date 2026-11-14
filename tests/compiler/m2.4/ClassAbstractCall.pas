// covers: LANG-622, ABI-103, CMP-366
program ClassAbstractCall;

{ Calling an abstract method that the run-time class does not override: its VMT slot holds System._AbstractError,
  which calls AbstractErrorProc when assigned and then ends the program with runtime error 211 (message on stderr). }

type
  TBase = class
    procedure Run; virtual; abstract;
    procedure Other; virtual;
  end;

  TPartial = class(TBase)
    procedure Other; override;
  end;

procedure TBase.Other;
begin
  Writeln('base other');
end;

procedure TPartial.Other;
begin
  Writeln('partial other');
end;

procedure Hook;
begin
  Writeln('abstract hook');
end;

var
  B: TBase;
begin
  B := TPartial.Create;
  B.Other;
  AbstractErrorProc := Hook;
  B.Run;
  Writeln('not reached');
end.
