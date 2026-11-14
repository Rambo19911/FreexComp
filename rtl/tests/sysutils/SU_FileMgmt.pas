// covers: RTL-261
// opc-run: tempdir
program SU_FileMgmt;
{ sysutils.md section 8.1: FileExists, DirectoryExists, DeleteFile, RenameFile, CreateDir, RemoveDir, SetCurrentDir,
  GetCurrentDir (no trailing delimiter except a root), ForceDirectories (all levels; '' raises EInOutError), FileAge
  (local last-write time), FileGetAttr / FileSetAttr, GetTempDir (trailing delimiter), GetModuleName. Runs in a fresh
  temporary directory. }
uses
  Windows, SysUtils, TestKit;

procedure MakeFile(const Name: string);
var
  H: THandle;
begin
  H := FileCreate(Name);
  FileClose(H);
end;

procedure TestExistence;
begin
  Section('exists, delete, rename');
  MakeFile('a.txt');
  CheckEqB(True, FileExists('a.txt'), 'FileExists');
  CheckEqB(False, FileExists('b.txt'), 'FileExists missing');
  CheckEqB(False, DirectoryExists('a.txt'), 'a file is no directory');
  CheckEqB(True, RenameFile('a.txt', 'b.txt'), 'RenameFile');
  CheckEqB(False, FileExists('a.txt'), 'old name gone');
  CheckEqB(True, FileExists('b.txt'), 'new name there');
  CheckEqB(False, RenameFile('a.txt', 'c.txt'), 'RenameFile missing');
  CheckEqB(True, DeleteFile('b.txt'), 'DeleteFile');
  CheckEqB(False, DeleteFile('b.txt'), 'DeleteFile missing');
  CheckEqB(False, FileExists(''), 'FileExists empty');
end;

procedure TestDirectories;
var
  Start: string;
begin
  Section('directories');
  Start := GetCurrentDir;
  Check((Start <> '') and (Start[Length(Start)] <> '\'), 'GetCurrentDir without a trailing delimiter');
  CheckEqB(True, CreateDir('sub'), 'CreateDir');
  CheckEqB(False, CreateDir('sub'), 'CreateDir existing');
  CheckEqB(True, DirectoryExists('sub'), 'DirectoryExists');
  CheckEqB(False, FileExists('sub'), 'a directory is no file');
  CheckEqB(True, SetCurrentDir('sub'), 'SetCurrentDir');
  CheckEqS(IncludeTrailingPathDelimiter(Start) + 'sub', GetCurrentDir, 'GetCurrentDir after SetCurrentDir');
  CheckEqB(True, SetCurrentDir('..'), 'SetCurrentDir ..');
  CheckEqB(False, SetCurrentDir('nowhere'), 'SetCurrentDir missing');
  CheckEqB(True, RemoveDir('sub'), 'RemoveDir');
  CheckEqB(False, RemoveDir('sub'), 'RemoveDir missing');
  CheckEqB(True, ForceDirectories('x\y\z'), 'ForceDirectories');
  CheckEqB(True, DirectoryExists('x\y\z'), 'all levels created');
  CheckEqB(True, ForceDirectories('x\y\z'), 'ForceDirectories existing');
  CheckEqB(True, ForceDirectories('x/y2/'), 'ForceDirectories with / and a trailing delimiter');
  CheckEqB(True, DirectoryExists('x\y2'), 'created with /');
  CheckEqB(True, ForceDirectories(IncludeTrailingPathDelimiter(Start) + 'abs\olute'), 'ForceDirectories absolute');
  CheckEqB(True, DirectoryExists('abs\olute'), 'absolute levels created');
  SetCurrentDir('C:\');
  CheckEqS('C:\', GetCurrentDir, 'GetCurrentDir of a root keeps the delimiter');
  SetCurrentDir(Start);
end;

procedure TestForceEmpty;
var
  Msg: string;
begin
  Section('ForceDirectories('''')');
  Msg := 'no exception';
  try
    ForceDirectories('');
  except
    on E: EInOutError do
      Msg := E.ClassName + ': ' + E.Message;
  end;
  CheckEqS('EInOutError: Cannot create directory', Msg, 'raises');
end;

procedure TestAttributes;
var
  DT, Now_: TDateTime;
  A: Integer;
begin
  Section('attributes and age');
  MakeFile('attr.txt');
  A := FileGetAttr('attr.txt');
  Check((A <> -1) and (A and faDirectory = 0), 'FileGetAttr of a file');
  CheckEqI(-1, FileGetAttr('missing.txt'), 'FileGetAttr missing');
  Check(FileGetAttr('.') and faDirectory <> 0, 'FileGetAttr of a directory');
  CheckEqI(0, FileSetAttr('attr.txt', faReadOnly), 'FileSetAttr');
  Check(FileGetAttr('attr.txt') and faReadOnly <> 0, 'read-only set');
  CheckEqB(False, DeleteFile('attr.txt'), 'read-only file cannot be deleted');
  CheckEqI(0, FileSetAttr('attr.txt', faNormal), 'FileSetAttr normal');
  CheckEqI(2, FileSetAttr('missing.txt', faNormal), 'FileSetAttr missing: Win32 error 2');
  Now_ := Now;
  CheckEqB(True, FileAge('attr.txt', DT), 'FileAge');
  Check(Abs(DT - Now_) < 1 / 24, 'FileAge is local time, close to Now');
  CheckEqB(False, FileAge('missing.txt', DT), 'FileAge missing');
  CheckEqB(True, DeleteFile('attr.txt'), 'deleted');
end;

procedure TestPaths;
var
  T: string;
begin
  Section('GetTempDir, GetModuleName');
  T := GetTempDir;
  Check((T <> '') and (T[Length(T)] = '\'), 'GetTempDir ends with a delimiter');
  CheckEqB(True, DirectoryExists(T), 'GetTempDir exists');
  CheckEqS(ParamStr(0), GetModuleName(0), 'GetModuleName(0) = the program');
  CheckEqS('SU_FileMgmt.exe', ExtractFileName(GetModuleName(HInstance)), 'GetModuleName(HInstance)');
end;

begin
  TestExistence;
  TestDirectories;
  TestForceEmpty;
  TestAttributes;
  TestPaths;
  Done;
end.
