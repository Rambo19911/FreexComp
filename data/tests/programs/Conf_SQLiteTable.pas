// covers: DAT-153, DAT-154, DAT-155, DAT-160, DAT-161
program Conf_SQLiteTable;
{ The dataset conformance suite on a SQLite table. }
uses
  SysUtils, Classes, DB, SQLiteData, DataTestUtils, Conformance;

var
  C: TSQLiteConnection;
  T: TSQLiteTable;
begin
  C := TSQLiteConnection.Create(nil);
  C.Database := ':memory:';
  C.Open;
  C.ExecSQL('CREATE TABLE conf (id INTEGER PRIMARY KEY, name VARCHAR(50) NOT NULL, amount FLOAT, born DATE, ' +
    'flag BOOLEAN, note TEXT)');
  T := TSQLiteTable.Create(nil);
  T.Connection := C;
  T.TableName := 'conf';
  T.Open;
  RunConformance(T, 'TSQLiteTable');
  T.Free;
  C.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
