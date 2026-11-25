// phase: 8
// covers: RTL-690
// opc-run: tempdir
program IO_PathFileDir;
{ IOUtils (other-units.md section 10, RTL-690): TPath (text operations, full path, temporary names, known folders),
  TFile (existence, delete, copy, move, text / bytes / lines with encodings, size, time stamp, streams) and
  TDirectory (existence, create, delete, enumerate, current directory, move); the error classes: a missing file
  EFileNotFoundException, a missing directory EDirectoryNotFoundException, any other failure EInOutError with
  the Win32 error in ErrorCode. }
uses
  SysUtils, Classes, Types, IOUtils, TestKit, IOKit;

var
  Dir: string;

procedure PathTests;
var
  S, T: string;
begin
  Section('TPath text operations');
  CheckEqS('a\b', TPath.Combine('a', 'b'), 'Combine adds the separator');
  CheckEqS('a\b', TPath.Combine('a\', 'b'), 'Combine keeps one separator');
  CheckEqS('a/b', TPath.Combine('a/', 'b'), 'Combine accepts a trailing slash');
  CheckEqS('C:b', TPath.Combine('C:', 'b'), 'Combine after a drive adds nothing');
  CheckEqS('b', TPath.Combine('', 'b'), 'Combine with an empty first part');
  CheckEqS('a', TPath.Combine('a', ''), 'Combine with an empty second part');
  CheckEqS('C:\x', TPath.Combine('a', 'C:\x'), 'Combine: a rooted second part wins');
  CheckEqS('\x', TPath.Combine('a', '\x'), 'Combine: a second part from the root wins');
  CheckEqS('c.txt', TPath.GetFileName('a\b\c.txt'), 'GetFileName');
  CheckEqS('', TPath.GetFileName('a\b\'), 'GetFileName of a directory path');
  CheckEqS('c', TPath.GetFileNameWithoutExtension('a\b\c.txt'), 'GetFileNameWithoutExtension');
  CheckEqS('c.tar', TPath.GetFileNameWithoutExtension('c.tar.gz'), 'GetFileNameWithoutExtension, two dots');
  CheckEqS('.txt', TPath.GetExtension('a\b\c.txt'), 'GetExtension');
  CheckEqS('', TPath.GetExtension('a.b\c'), 'GetExtension: a dot in a directory name');
  CheckEqS('a\b', TPath.GetDirectoryName('a\b\c.txt'), 'GetDirectoryName');
  CheckEqS('C:\', TPath.GetDirectoryName('C:\c.txt'), 'GetDirectoryName keeps a root');
  CheckEqS('', TPath.GetDirectoryName('c.txt'), 'GetDirectoryName of a bare name');
  CheckEqS('a\c.bak', TPath.ChangeExtension('a\c.txt', '.bak'), 'ChangeExtension');
  CheckEqS('a\c.bak', TPath.ChangeExtension('a\c.txt', 'bak'), 'ChangeExtension adds the dot');
  CheckEqS('a\c', TPath.ChangeExtension('a\c.txt', ''), 'ChangeExtension removes');
  CheckEqS('a\c.bak', TPath.ChangeExtension('a\c', '.bak'), 'ChangeExtension appends');
  CheckEqB(True, TPath.HasExtension('a\c.txt'), 'HasExtension');
  CheckEqB(False, TPath.HasExtension('a.b\c'), 'HasExtension: none');
  CheckEqB(False, TPath.HasExtension('a\c.'), 'HasExtension: a final dot is none');
  CheckEqB(True, TPath.IsPathRooted('C:\a'), 'IsPathRooted drive');
  CheckEqB(True, TPath.IsPathRooted('C:a'), 'IsPathRooted drive-relative');
  CheckEqB(True, TPath.IsPathRooted('\a'), 'IsPathRooted root');
  CheckEqB(True, TPath.IsPathRooted('/a'), 'IsPathRooted slash');
  CheckEqB(True, TPath.IsPathRooted('\\srv\share\a'), 'IsPathRooted share');
  CheckEqB(False, TPath.IsPathRooted('a\b'), 'IsPathRooted relative');
  CheckEqB(False, TPath.IsPathRooted(''), 'IsPathRooted empty');
  CheckEqS('\', TPath.DirectorySeparatorChar, 'DirectorySeparatorChar');

  Section('TPath and the file system');
  CheckEqS(ExpandFileName(Dir + 'x\..\y.txt'), TPath.GetFullPath(Dir + 'x\..\y.txt'), 'GetFullPath');
  CheckEqS(Dir + 'y.txt', TPath.GetFullPath(Dir + 'x\..\y.txt'), 'GetFullPath removes ..');
  CheckEqS(GetTempDir, TPath.GetTempPath, 'GetTempPath');
  Check(IsPathDelimiter(TPath.GetTempPath, Length(TPath.GetTempPath)), 'GetTempPath ends with the separator');
  S := TPath.GetTempFileName;
  T := TPath.GetTempFileName;
  Check(TFile.Exists(S) and TFile.Exists(T), 'GetTempFileName creates the file');
  CheckEqI(0, TFile.GetSize(S), 'the temporary file is empty');
  Check(S <> T, 'GetTempFileName names differ');
  CheckEqS(TPath.GetTempPath, ExtractFilePath(S), 'the temporary file is in the temporary folder');
  CheckEqS('.tmp', TPath.GetExtension(S), 'the temporary file has the extension .tmp');
  TFile.Delete(S);
  TFile.Delete(T);
  Check(not TFile.Exists(S) and not TFile.Exists(T), 'temporary files deleted');
  Check(TDirectory.Exists(TPath.GetHomePath), 'GetHomePath exists');
  Check(TPath.IsPathRooted(TPath.GetDocumentsPath), 'GetDocumentsPath is rooted');
  Check(not IsPathDelimiter(TPath.GetHomePath, Length(TPath.GetHomePath)), 'GetHomePath has no trailing separator');
end;

procedure FileTests;
var
  B: TBytes;
  L: TStringDynArray;
  S: TFileStream;
  A, C, Missing: string;
  T: TDateTime;
  E2: Integer;
begin
  A := Dir + 'a.txt';
  C := Dir + 'c.txt';
  Missing := Dir + 'missing.txt';

  Section('TFile text');
  CheckEqB(False, TFile.Exists(A), 'Exists: not yet');
  TFile.WriteAllText(A, 'h'#$00E9'llo');
  CheckEqB(True, TFile.Exists(A), 'Exists');
  CheckEqB(False, TFile.Exists(Dir), 'Exists is False for a directory');
  CheckEqS('68C3A96C6C6F', Hex(TFile.ReadAllBytes(A)), 'WriteAllText: UTF-8 without BOM');
  CheckEqS('h'#$00E9'llo', TFile.ReadAllText(A), 'ReadAllText');
  TFile.WriteAllText(A, 'h'#$00E9, TEncoding.UTF8);
  CheckEqS('EFBBBF68C3A9', Hex(TFile.ReadAllBytes(A)), 'WriteAllText with an encoding writes its preamble');
  CheckEqS('h'#$00E9, TFile.ReadAllText(A), 'ReadAllText skips the UTF-8 BOM');
  CheckEqS('h'#$00E9, TFile.ReadAllText(A, TEncoding.UTF8), 'ReadAllText with the encoding skips its BOM');
  TFile.WriteAllText(A, 'h'#$00E9, TEncoding.Unicode);
  CheckEqS('FFFE6800E900', Hex(TFile.ReadAllBytes(A)), 'WriteAllText UTF-16');
  CheckEqS('h'#$00E9, TFile.ReadAllText(A), 'ReadAllText detects UTF-16');
  TFile.WriteAllText(A, '');
  CheckEqI(0, TFile.GetSize(A), 'WriteAllText of nothing truncates');
  CheckEqS('', TFile.ReadAllText(A), 'ReadAllText of an empty file');
  TFile.AppendAllText(A, 'one');
  TFile.AppendAllText(A, #$00E9);
  CheckEqS('6F6E65C3A9', Hex(TFile.ReadAllBytes(A)), 'AppendAllText appends UTF-8');
  TFile.AppendAllText(C, 'x', TEncoding.Unicode);
  TFile.AppendAllText(C, 'y', TEncoding.Unicode);
  CheckEqS('FFFE78007900', Hex(TFile.ReadAllBytes(C)), 'AppendAllText creates; the preamble only into an empty file');

  Section('TFile bytes and lines');
  SetLength(B, 4);
  B[0] := 0;
  B[1] := 255;
  B[2] := 13;
  B[3] := 10;
  TFile.WriteAllBytes(A, B);
  CheckEqS('00FF0D0A', Hex(TFile.ReadAllBytes(A)), 'WriteAllBytes / ReadAllBytes');
  CheckEqI(4, TFile.GetSize(A), 'GetSize');
  SetLength(B, 0);
  TFile.WriteAllBytes(A, B);
  CheckEqI(0, Length(TFile.ReadAllBytes(A)), 'no bytes');
  SetLength(L, 3);
  L[0] := 'one';
  L[1] := '';
  L[2] := 'thr'#$00E9'e';
  TFile.WriteAllLines(A, L);
  CheckEqS('6F6E650D0A0D0A746872C3A9650D0A', Hex(TFile.ReadAllBytes(A)), 'WriteAllLines: CRLF after every line');
  CheckEqS('3:[one][][thr'#$00E9'e]', Joined(TFile.ReadAllLines(A)), 'ReadAllLines');
  TFile.WriteAllText(A, 'a'#10'b'#13'c'#13#10#13#10'd');
  CheckEqS('5:[a][b][c][][d]', Joined(TFile.ReadAllLines(A)), 'ReadAllLines: LF, CR and CRLF end a line');
  TFile.WriteAllText(A, '');
  CheckEqS('0:', Joined(TFile.ReadAllLines(A)), 'ReadAllLines of an empty file');
  TFile.WriteAllLines(A, L, TEncoding.Unicode);
  CheckEqI(2 + 2 * (5 + 2 + 7), TFile.GetSize(A), 'WriteAllLines with an encoding');
  CheckEqS('3:[one][][thr'#$00E9'e]', Joined(TFile.ReadAllLines(A, TEncoding.Unicode)), 'ReadAllLines with an encoding');

  Section('TFile copy, move, delete');
  TFile.WriteAllText(A, 'A');
  TFile.WriteAllText(C, 'C');
  E2 := -1;
  try TFile.Copy(A, C); except on E: EInOutError do E2 := E.ErrorCode; end;
  CheckEqI(80, E2, 'Copy onto an existing file: EInOutError, ERROR_FILE_EXISTS');
  CheckEqS('C', TFile.ReadAllText(C), 'the target is unchanged');
  TFile.Copy(A, C, True);
  CheckEqS('A', TFile.ReadAllText(C), 'Copy with Overwrite');
  TFile.Delete(C);
  CheckEqB(False, TFile.Exists(C), 'Delete');
  TFile.Delete(C);
  Check(True, 'Delete of a missing file is silent');
  TFile.Copy(A, C);
  CheckEqS('A', TFile.ReadAllText(C), 'Copy to a new name');
  TFile.Delete(C);
  TFile.Move(A, C);
  Check(TFile.Exists(C) and not TFile.Exists(A), 'Move');
  TFile.WriteAllText(A, 'A2');
  E2 := -1;
  try TFile.Move(A, C); except on E: EInOutError do E2 := E.ErrorCode; end;
  CheckEqI(183, E2, 'Move onto an existing file: EInOutError, ERROR_ALREADY_EXISTS');

  Section('TFile streams and time');
  S := TFile.Create(A);
  try
    CheckEqI(0, S.Size, 'Create truncates');
    WriteRaw(S, 'abcdef');
  finally
    S.Free;
  end;
  S := TFile.OpenWrite(A);
  try
    CheckEqI(6, S.Size, 'OpenWrite keeps the content');
    WriteRaw(S, 'XY');
  finally
    S.Free;
  end;
  S := TFile.OpenRead(A);
  try
    CheckEqI(6, S.Size, 'OpenRead');
  finally
    S.Free;
  end;
  CheckEqS('XYcdef', TFile.ReadAllText(A), 'OpenWrite writes from the start');
  TFile.Delete(A);
  S := TFile.OpenWrite(A);
  S.Free;
  CheckEqB(True, TFile.Exists(A), 'OpenWrite creates a missing file');
  Check(FileAge(A, T) and (T = TFile.GetLastWriteTime(A)), 'GetLastWriteTime is the local time of FileAge');
  Check(Abs(TFile.GetLastWriteTime(A) - Now) < 1, 'GetLastWriteTime is recent');

  Section('TFile errors');
  Expect('EFileNotFoundException', 'File "' + Missing + '" not found');
  try TFile.ReadAllText(Missing); except on E: Exception do CheckRaises('ReadAllText', E.ClassName, E.Message); end;
  try TFile.ReadAllBytes(Missing); except on E: Exception do CheckRaises('ReadAllBytes', E.ClassName, E.Message); end;
  try TFile.ReadAllLines(Missing); except on E: Exception do CheckRaises('ReadAllLines', E.ClassName, E.Message); end;
  try TFile.Copy(Missing, C); except on E: Exception do CheckRaises('Copy', E.ClassName, E.Message); end;
  try TFile.Move(Missing, A); except on E: Exception do CheckRaises('Move', E.ClassName, E.Message); end;
  try TFile.GetSize(Missing); except on E: Exception do CheckRaises('GetSize', E.ClassName, E.Message); end;
  try TFile.GetLastWriteTime(Missing); except on E: Exception do CheckRaises('GetLastWriteTime', E.ClassName, E.Message); end;
  try TFile.OpenRead(Missing).Free; except on E: Exception do CheckRaises('OpenRead', E.ClassName, E.Message); end;
  Expect('EDirectoryNotFoundException', 'Directory "' + Dir + 'nodir" not found');
  try TFile.WriteAllText(Dir + 'nodir\a.txt', 'x'); except on E: Exception do CheckRaises('WriteAllText', E.ClassName, E.Message); end;
  try TFile.Create(Dir + 'nodir\a.txt').Free; except on E: Exception do CheckRaises('Create', E.ClassName, E.Message); end;
  try TFile.ReadAllText(Dir + 'nodir\a.txt'); except on E: Exception do CheckRaises('ReadAllText in a missing directory', E.ClassName, E.Message); end;
  TDirectory.CreateDirectory(Dir + 'sub');
  E2 := -1;
  try TFile.Delete(Dir + 'sub'); except on E: EInOutError do E2 := E.ErrorCode; end;
  CheckEqI(5, E2, 'Delete of a directory: EInOutError, ERROR_ACCESS_DENIED');
  TDirectory.Delete(Dir + 'sub');
  TFile.Delete(A);
  TFile.Delete(C);
end;

procedure DirTests;
var
  Root, Saved: string;
  E2: Integer;
begin
  Root := Dir + 'tree';
  Section('TDirectory create and enumerate');
  CheckEqB(False, TDirectory.Exists(Root), 'Exists: not yet');
  TDirectory.CreateDirectory(Root + '\one\deep');
  CheckEqB(True, TDirectory.Exists(Root + '\one\deep'), 'CreateDirectory creates every level');
  TDirectory.CreateDirectory(Root + '\one');
  Check(True, 'CreateDirectory of an existing directory is silent');
  TDirectory.CreateDirectory(Root + '\two');
  TFile.WriteAllText(Root + '\a.txt', '');
  TFile.WriteAllText(Root + '\b.log', '');
  TFile.WriteAllText(Root + '\one\c.txt', '');
  TFile.WriteAllText(Root + '\one\deep\d.txt', '');
  CheckEqB(False, TDirectory.Exists(Root + '\a.txt'), 'Exists is False for a file');
  CheckEqS('a.txt|b.log', Names(TDirectory.GetFiles(Root), Root + '\'), 'GetFiles');
  CheckEqS('a.txt|b.log', Names(TDirectory.GetFiles(Root + '\'), Root + '\'), 'GetFiles, trailing separator');
  CheckEqS('a.txt', Names(TDirectory.GetFiles(Root, '*.txt'), Root + '\'), 'GetFiles with a pattern');
  CheckEqS('', Names(TDirectory.GetFiles(Root, '*.none'), Root + '\'), 'GetFiles: no match');
  CheckEqS('a.txt', Names(TDirectory.GetFiles(Root, '*.txt', soTopDirectoryOnly), Root + '\'), 'GetFiles, top directory only');
  CheckEqS('a.txt|one\c.txt|one\deep\d.txt', Names(TDirectory.GetFiles(Root, '*.txt', soAllDirectories), Root + '\'),
    'GetFiles, all directories');
  CheckEqS('a.txt|b.log|one\c.txt|one\deep\d.txt', Names(TDirectory.GetFiles(Root, '*', soAllDirectories), Root + '\'),
    'GetFiles, all files of all directories');
  CheckEqS('one|two', Names(TDirectory.GetDirectories(Root), Root + '\'), 'GetDirectories');
  CheckEqS('one', Names(TDirectory.GetDirectories(Root, 'o*'), Root + '\'), 'GetDirectories with a pattern');
  CheckEqS('one|one\deep|two', Names(TDirectory.GetDirectories(Root, '*', soAllDirectories), Root + '\'),
    'GetDirectories, all directories');
  CheckEqS('one\deep', Names(TDirectory.GetDirectories(Root, 'd*', soAllDirectories), Root + '\'),
    'GetDirectories: the pattern does not filter the directories searched');

  Section('TDirectory current directory, move, delete');
  Saved := TDirectory.GetCurrentDirectory;
  CheckEqS(GetCurrentDir, Saved, 'GetCurrentDirectory');
  TDirectory.SetCurrentDirectory(Root);
  CheckEqS(Root, TDirectory.GetCurrentDirectory, 'SetCurrentDirectory');
  TDirectory.SetCurrentDirectory(Saved);
  TDirectory.Move(Root + '\two', Root + '\three');
  Check(TDirectory.Exists(Root + '\three') and not TDirectory.Exists(Root + '\two'), 'Move');
  TDirectory.Delete(Root + '\three');
  CheckEqB(False, TDirectory.Exists(Root + '\three'), 'Delete of an empty directory');
  E2 := -1;
  try TDirectory.Delete(Root); except on E: EInOutError do E2 := E.ErrorCode; end;
  CheckEqI(145, E2, 'Delete of a directory with content: EInOutError, ERROR_DIR_NOT_EMPTY');
  E2 := -1;
  try TDirectory.Delete(Root, False); except on E: EInOutError do E2 := E.ErrorCode; end;
  CheckEqI(145, E2, 'Delete, not recursive');
  CheckEqB(True, TFile.Exists(Root + '\one\deep\d.txt'), 'nothing was deleted');
  TDirectory.Delete(Root, True);
  CheckEqB(False, TDirectory.Exists(Root), 'Delete, recursive');

  Section('TDirectory errors');
  Expect('EDirectoryNotFoundException', 'Directory "' + Root + '" not found');
  try TDirectory.Delete(Root); except on E: Exception do CheckRaises('Delete', E.ClassName, E.Message); end;
  try TDirectory.Delete(Root, True); except on E: Exception do CheckRaises('Delete, recursive', E.ClassName, E.Message); end;
  try TDirectory.GetFiles(Root); except on E: Exception do CheckRaises('GetFiles', E.ClassName, E.Message); end;
  try TDirectory.GetDirectories(Root); except on E: Exception do CheckRaises('GetDirectories', E.ClassName, E.Message); end;
  try TDirectory.SetCurrentDirectory(Root); except on E: Exception do CheckRaises('SetCurrentDirectory', E.ClassName, E.Message); end;
  try TDirectory.Move(Root, Root + '2'); except on E: Exception do CheckRaises('Move', E.ClassName, E.Message); end;
  TFile.WriteAllText(Dir + 'f.txt', '');
  E2 := -1;
  try TDirectory.CreateDirectory(Dir + 'f.txt'); except on E: EInOutError do E2 := E.ErrorCode; end;
  CheckEqI(183, E2, 'CreateDirectory over a file: EInOutError, ERROR_ALREADY_EXISTS');
  TFile.Delete(Dir + 'f.txt');
end;

begin
  Dir := IncludeTrailingPathDelimiter(ParamStr(1));
  PathTests;
  FileTests;
  DirTests;
  Done;
end.
