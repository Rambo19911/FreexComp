unit LongPathKit;
{ Shared body of winapi/LongPaths and winapi/LongPaths_Unaware (platform-layer.md "Long paths"): the file routines of
  the RTL with a full path of 300 or more characters and no extended-length prefix. Works = the operating system
  accepts such paths for this process (system policy on and the image declares the awareness); otherwise every call
  must be refused cleanly. Each check prints the same line in both cases. }

interface

{ the system long-path policy, read from the registry }
function LongPathPolicyOn: Boolean;
procedure RunLongPaths(const Root: string; Works: Boolean);

implementation

uses
  Windows, SysUtils, Classes, IniFiles, TestKit;

const
  MaxLevels = 16;


var
  Levels: array[0..MaxLevels - 1] of string;
  LevelCount, Created: Integer;
  Deep: string;
  T: Text;

{ the policy value through the W names or through the unsuffixed aliases of the same functions }
function ReadPolicy(Aliases: Boolean): Boolean;
const
  KeyName = 'SYSTEM\CurrentControlSet\Control\FileSystem';
  ValueName = 'LongPathsEnabled';
var
  Key: HKEY;
  Value, Kind, Size: DWORD;
  Code: LONG;
begin
  Result := False;
  if Aliases then
    Code := RegOpenKeyEx(HKEY_LOCAL_MACHINE, KeyName, 0, KEY_QUERY_VALUE, Key)
  else
    Code := RegOpenKeyExW(HKEY_LOCAL_MACHINE, KeyName, 0, KEY_QUERY_VALUE, Key);
  if Code <> 0 then
    Exit;
  Value := 0;
  Kind := 0;
  Size := SizeOf(Value);
  if Aliases then
    Code := RegQueryValueEx(Key, ValueName, nil, @Kind, PByte(@Value), @Size)
  else
    Code := RegQueryValueExW(Key, ValueName, nil, @Kind, PByte(@Value), @Size);
  if Code = 0 then
    Result := (Kind = REG_DWORD) and (Value = 1);
  RegCloseKey(Key);
end;

function LongPathPolicyOn: Boolean;
begin
  Result := ReadPolicy(False);
end;

{ the operating system's answer to a path it does not accept }
function Refused(Error: Cardinal): Boolean;
begin
  Result := (Error = ERROR_PATH_NOT_FOUND) or (Error = ERROR_FILENAME_EXCED_RANGE);
end;

function Segment(Index: Integer): string;
begin
  Result := 'seg' + IntToStr(Index) + StringOfChar('x', 46);
end;

procedure PlanLevels(const Root: string);
begin
  LevelCount := 0;
  Deep := Root;
  while (Length(Deep) < 300) and (LevelCount < MaxLevels) do
  begin
    Deep := Deep + '\' + Segment(LevelCount);
    Levels[LevelCount] := Deep;
    LevelCount := LevelCount + 1;
  end;
end;

function TreeAsExpected(Works: Boolean): Boolean;
var
  Error: Cardinal;
begin
  Created := 0;
  Error := 0;
  while Created < LevelCount do
  begin
    if not CreateDir(Levels[Created]) then
    begin
      Error := GetLastError;
      Break;
    end;
    Created := Created + 1;
  end;
  if Works then
    Result := (Created = LevelCount) and (Length(Deep) >= 300) and DirectoryExists(Deep)
  else
    Result := (Created < LevelCount) and Refused(Error) and not DirectoryExists(Deep);
end;

function ForceAsExpected(Works: Boolean): Boolean;
begin
  if Works then
    Result := ForceDirectories(Deep + '\forced\twice') and DirectoryExists(Deep + '\forced\twice') and
      RemoveDir(Deep + '\forced\twice') and RemoveDir(Deep + '\forced')
  else
    Result := not ForceDirectories(Deep + '\forced\twice') and not RemoveDir(Deep + '\forced');
end;

function HandleFileAsExpected(const Name: string; Works: Boolean): Boolean;
var
  H: THandle;
  Data, Back: array[0..4] of Byte;
  I: Integer;
begin
  for I := 0 to 4 do
  begin
    Data[I] := Byte(I + 65);
    Back[I] := 0;
  end;
  H := FileCreate(Name);
  if not Works then
  begin
    Result := (H = INVALID_HANDLE_VALUE) and Refused(GetLastError) and (FileOpen(Name, fmOpenRead) = INVALID_HANDLE_VALUE);
    Exit;
  end;
  Result := (H <> INVALID_HANDLE_VALUE) and (FileWrite(H, Data, 5) = 5);
  if H <> INVALID_HANDLE_VALUE then
    FileClose(H);
  H := FileOpen(Name, fmOpenRead);
  Result := Result and (H <> INVALID_HANDLE_VALUE) and (FileRead(H, Back, 5) = 5) and (Back[0] = 65) and (Back[4] = 69);
  if H <> INVALID_HANDLE_VALUE then
    FileClose(H);
end;

function FindAsExpected(const Dir: string; Works: Boolean): Boolean;
var
  F: TSearchRec;
  Code: Integer;
begin
  Code := FindFirst(Dir + '\*.txt', faAnyFile, F);
  if Works then
    Result := (Code = 0) and (F.Name = 'file.txt') and (F.Size = 5) and (FindNext(F) <> 0)
  else
    Result := Code <> 0;
  FindClose(F);
end;

function StreamAsExpected(const Name: string; Works: Boolean): Boolean;
var
  S: TFileStream;
  Value: Integer;
  Raised: Boolean;
begin
  Raised := False;
  Value := 0;
  try
    S := TFileStream.Create(Name, fmCreate);
    try
      Value := 123456;
      S.WriteBuffer(Value, SizeOf(Value));
    finally
      S.Free;
    end;
    Value := 0;
    S := TFileStream.Create(Name, fmOpenRead);
    try
      S.ReadBuffer(Value, SizeOf(Value));
    finally
      S.Free;
    end;
  except
    on E: EFCreateError do
      Raised := True;
  end;
  if Works then
    Result := not Raised and (Value = 123456) and DeleteFile(Name)
  else
    Result := Raised and not DeleteFile(Name);
end;

{$I-}
{ every IOResult is read into Code first: a pending error must never be left behind by a short-circuited "and" }
function TextAsExpected(const Name, NewName: string; Works: Boolean): Boolean;
var
  Line: string;
  Code: Integer;
begin
  Line := '';
  AssignFile(T, Name);
  Rewrite(T);
  Code := IOResult;
  if not Works then
  begin
    Result := Code <> 0;
    Reset(T);
    Code := IOResult;
    Result := Result and (Code <> 0);
    Erase(T);
    Code := IOResult;
    Result := Result and (Code <> 0);
    AssignFile(T, '');
    Exit;
  end;
  Result := Code = 0;
  Writeln(T, 'long path text');
  CloseFile(T);
  Code := IOResult;
  Result := Result and (Code = 0);
  Rename(T, NewName);
  Code := IOResult;
  Result := Result and (Code = 0) and FileExists(NewName) and not FileExists(Name);
  Reset(T);
  Code := IOResult;
  Result := Result and (Code = 0);
  Readln(T, Line);
  CloseFile(T);
  Code := IOResult;
  Result := Result and (Code = 0) and (Line = 'long path text');
  Erase(T);
  Code := IOResult;
  Result := Result and (Code = 0) and not FileExists(NewName);
  AssignFile(T, '');                   { releases the name }
end;
{$I+}

function IniAsExpected(const Name: string; Works: Boolean): Boolean;
var
  Ini: TIniFile;
  Raised: Boolean;
  Value: string;
begin
  Raised := False;
  Value := '';
  try
    Ini := TIniFile.Create(Name);
    try
      Ini.WriteString('s', 'k', 'v');
      Ini.UpdateFile;
    finally
      Ini.Free;
    end;
    Ini := TIniFile.Create(Name);
    try
      Value := Ini.ReadString('s', 'k', '');
    finally
      Ini.Free;
    end;
  except
    on E: Exception do
      Raised := True;
  end;
  if Works then
    Result := not Raised and (Value = 'v') and DeleteFile(Name)
  else
    Result := Raised and not FileExists(Name);
end;

function CurrentDirAsExpected(const Root: string; Works: Boolean): Boolean;
begin
  if Works then
    Result := SetCurrentDir(Deep) and (GetCurrentDir = Deep) and (ExpandFileName('rel.txt') = Deep + '\rel.txt')
  else
    Result := not SetCurrentDir(Deep) and Refused(GetLastError) and (GetCurrentDir = Root);
  Result := SetCurrentDir(Root) and Result;
end;

function RemoveTree: Boolean;
begin
  Result := True;
  while Created > 0 do
  begin
    Created := Created - 1;
    Result := RemoveDir(Levels[Created]) and Result;
  end;
  Result := Result and not DirectoryExists(Levels[0]);
end;

procedure RunLongPaths(const Root: string; Works: Boolean);
var
  A, B: string;
begin
  PlanLevels(Root);
  A := Deep + '\file.txt';
  B := Deep + '\renamed.txt';

  Section('policy');
  CheckEqB(ReadPolicy(False), ReadPolicy(True), 'registry read, W names and aliases');

  Section('directories');
  Check(SetCurrentDir(Root), 'start directory');
  Check(TreeAsExpected(Works), 'CreateDir down to 300 characters');
  Check(ForceAsExpected(Works), 'ForceDirectories, RemoveDir');
  Check(CurrentDirAsExpected(Root, Works), 'SetCurrentDir, GetCurrentDir, ExpandFileName of a relative name');

  Section('full names');
  CheckEqS(B, ExpandFileName(Deep + '\sub\..\renamed.txt'), 'ExpandFileName of a long name');
  CheckEqS(ParamStr(0), GetModuleName(0), 'GetModuleName');

  Section('files');
  Check(HandleFileAsExpected(A, Works), 'FileCreate, FileWrite, FileOpen, FileRead');
  CheckEqB(Works, FileExists(A), 'FileExists');
  Check(FindAsExpected(Deep, Works), 'FindFirst, FindNext');
  CheckEqB(Works, RenameFile(A, B), 'RenameFile');
  CheckEqB(Works, FileExists(B) and not FileExists(A), 'renamed');
  CheckEqB(Works, DeleteFile(B), 'DeleteFile');
  CheckEqB(False, FileExists(B), 'deleted');
  Check(StreamAsExpected(Deep + '\stream.bin', Works), 'TFileStream');
  Check(TextAsExpected(Deep + '\text.txt', Deep + '\text2.txt', Works), 'text file: Rewrite, Rename, Reset, Erase');
  Check(IniAsExpected(Deep + '\settings.ini', Works), 'TIniFile');

  Section('clean up');
  Check(RemoveTree, 'RemoveDir up to the start directory');
end;

end.
