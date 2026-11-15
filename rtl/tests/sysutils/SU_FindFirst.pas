// covers: RTL-262, RTL-263, RTL-264
// opc-run: tempdir
program SU_FindFirst;
{ sysutils.md section 8.1: FindFirst / FindNext / FindClose and TSearchRec (Name, Size, Attr, TimeStamp in local time).
  Result 0 = found, ERROR_NO_MORE_FILES (18) at the end, ERROR_FILE_NOT_FOUND (2) when nothing matches; entries with
  faHidden, faSysFile or faDirectory not asked for are skipped; '.' and '..' come with directories; FindClose twice is
  safe. Runs in a fresh temporary directory; names are sorted before they are compared. }
uses
  Windows, SysUtils, TestKit;

procedure MakeFile(const Name: string; Size: Integer);
var
  H: THandle;
  Bytes: AnsiString;
  Written: DWORD;
begin
  H := FileCreate(Name);
  Bytes := AnsiString(StringOfChar('x', Size));
  if Size > 0 then
    WriteFile(H, PAnsiChar(Bytes), Size, Written, nil);
  FileClose(H);
end;

{ the names found for Pattern/Attr, sorted, joined with spaces }
function Names(const Pattern: string; Attr: Integer): string;
var
  F: TSearchRec;
  List: array of string;
  I, J, R: Integer;
  T: string;
begin
  R := FindFirst(Pattern, Attr, F);
  while R = 0 do
  begin
    SetLength(List, Length(List) + 1);
    List[High(List)] := F.Name;
    R := FindNext(F);
  end;
  FindClose(F);
  for I := 0 to High(List) do
    for J := I + 1 to High(List) do
      if CompareStr(List[J], List[I]) < 0 then
      begin
        T := List[I];
        List[I] := List[J];
        List[J] := T;
      end;
  Result := '';
  for I := 0 to High(List) do
  begin
    if I > 0 then
      Result := Result + ' ';
    Result := Result + List[I];
  end;
end;

procedure TestFind;
var
  F: TSearchRec;
  R: Integer;
  Before: TDateTime;
begin
  Section('FindFirst / FindNext / FindClose');
  Before := Now;
  MakeFile('a.txt', 5);
  MakeFile('b.txt', 0);
  MakeFile('c.dat', 3);
  MakeFile('h.txt', 1);
  FileSetAttr('h.txt', faHidden);
  CreateDir('dir');
  CheckEqS('a.txt b.txt', Names('*.txt', 0), 'plain files only (hidden skipped)');
  CheckEqS('a.txt b.txt h.txt', Names('*.txt', faHidden), 'hidden asked for');
  CheckEqS('a.txt b.txt c.dat', Names('*', faArchive or faReadOnly), 'no directories without faDirectory');
  CheckEqS('. .. a.txt b.txt c.dat dir', Names('*', faDirectory), 'directories with . and ..');
  CheckEqS('. .. a.txt b.txt c.dat dir h.txt', Names('*', faAnyFile), 'faAnyFile');
  R := FindFirst('a.txt', faAnyFile, F);
  CheckEqI(0, R, 'found');
  CheckEqS('a.txt', F.Name, 'Name');
  CheckEqI(5, F.Size, 'Size');
  Check(F.Attr and faDirectory = 0, 'Attr of a file');
  Check(Abs(F.TimeStamp - Before) < 1 / 24, 'TimeStamp is local time');
  CheckEqI(18, FindNext(F), 'ERROR_NO_MORE_FILES at the end');
  FindClose(F);
  FindClose(F);
  Check(True, 'FindClose twice is safe');
  R := FindFirst('dir', faDirectory, F);
  CheckEqI(0, R, 'directory found');
  Check(F.Attr and faDirectory <> 0, 'Attr of a directory');
  FindClose(F);
  CheckEqI(2, FindFirst('*.none', faAnyFile, F), 'ERROR_FILE_NOT_FOUND');
  FindClose(F);
  CheckEqI(2, FindFirst('dir', 0, F), 'a directory that is not asked for');
  FindClose(F);
  Check(FindFirst('nowhere\*', faAnyFile, F) <> 0, 'missing directory');
  FindClose(F);
  FileSetAttr('h.txt', faNormal);
end;

begin
  { the temporary directory also holds the program and its object files }
  CreateDir('find');
  SetCurrentDir('find');
  TestFind;
  Done;
end.
