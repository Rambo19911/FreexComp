// covers: DAT-400, DAT-401, DAT-402
program Smoke;
{ Runner self-test (task A-001): compiled, run and compared by GoldenProgramTests. Exercises every helper of
  DataTestUtils, including the failure texts. }
uses
  SysUtils, DataTestUtils;

type
  TProbe = class
    procedure Raises;
    procedure Quiet;
  end;

procedure TProbe.Raises;
begin
  raise EConvertError.Create('bad value here');
end;

procedure TProbe.Quiet;
begin
end;

procedure RaisePlain;
begin
  raise Exception.Create('plain failure');
end;

var
  P: TProbe;
begin
  Section('helpers');
  Check(True, 'check true');
  CheckEq('abc', 'abc', 'equal strings');
  CheckEqInt(42, 42, 'equal integers');
  CheckEqBool(True, True, 'equal booleans');
  P := TProbe.Create;
  CheckRaises(P.Raises, 'EConvertError', 'bad value', 'method raises');
  CheckRaisesProc(RaisePlain, 'Exception', 'plain failure', 'procedure raises');
  Section('failure texts');
  Check(False, 'check false');
  CheckEq('abc', 'abd', 'different strings');
  CheckEqInt(1, 2, 'different integers');
  CheckEqBool(False, True, 'different booleans');
  CheckRaises(P.Quiet, 'EConvertError', '', 'nothing raised');
  CheckRaises(P.Raises, 'EAbort', 'bad value', 'wrong class');
  CheckRaises(P.Raises, 'EConvertError', 'other text', 'wrong message');
  P.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
