// covers: DAT-300, DAT-302, DAT-303, DAT-307
program Conf_MemTable;
{ The dataset conformance suite on the in-memory table. }
uses
  SysUtils, Classes, DB, MemData, DataTestUtils, Conformance;

var
  T: TMemTable;
begin
  T := TMemTable.Create(nil);
  T.FieldDefs.Add('id', ftAutoInc);
  T.FieldDefs.Add('name', ftString, 50, True);
  T.FieldDefs.Add('amount', ftFloat);
  T.FieldDefs.Add('born', ftDate);
  T.FieldDefs.Add('flag', ftBoolean);
  T.FieldDefs.Add('note', ftMemo);
  T.CreateDataSet;
  RunConformance(T, 'TMemTable');
  T.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
