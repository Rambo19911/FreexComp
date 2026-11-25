{ IOUtils - paths, files and directories as static record members (docs: rtl/other-units.md section 10, RTL-690).
  Thin layers over SysUtils and Classes, with kernel32 called directly where SysUtils has no routine.
  Errors: a missing file raises EFileNotFoundException, a missing directory EDirectoryNotFoundException, every other
  failure EInOutError with the text of the Win32 error and the code in ErrorCode. }
unit IOUtils;

interface

uses
  Windows, SysUtils, Classes, Types;

type
  TSearchOption = (soTopDirectoryOnly, soAllDirectories);

  { text operations accept '\' and '/'; no member checks the characters of a path }
  TPath = record
  public
    const DirectorySeparatorChar = '\';
    { P2 when it is rooted or P1 is empty, P1 when P2 is empty, else P1 + separator + P2 (no separator after a
      trailing '\', '/' or ':') }
    class function Combine(const P1, P2: string): string; static;
    class function GetFileName(const Path: string): string; static;
    class function GetFileNameWithoutExtension(const Path: string): string; static;
    { with the dot; '' when the name has none }
    class function GetExtension(const Path: string): string; static;
    { without the trailing separator, except for a root }
    class function GetDirectoryName(const Path: string): string; static;
    { Extension with or without the leading dot; '' removes the extension }
    class function ChangeExtension(const Path, Extension: string): string; static;
    class function GetFullPath(const Path: string): string; static;
    { the temporary folder of the user, with the trailing separator }
    class function GetTempPath: string; static;
    { creates an empty file with a new name tmpXXXXXXXX.tmp in the temporary folder and returns its full name }
    class function GetTempFileName: string; static;
    { False for a name that ends with its dot }
    class function HasExtension(const Path: string): Boolean; static;
    { starts with a separator or with a drive }
    class function IsPathRooted(const Path: string): Boolean; static;
    { the roaming application data folder of the user (environment variable APPDATA), no trailing separator }
    class function GetHomePath: string; static;
    { the folder Documents of the user profile (environment variable USERPROFILE), no trailing separator }
    class function GetDocumentsPath: string; static;
  end;

  { Encoding = nil: reading detects a UTF-8 / UTF-16 byte order mark, else UTF-8; writing is UTF-8 without a byte
    order mark. Encoding given: reading skips its byte order mark, writing starts with it }
  TFile = record
  public
    { False for a directory }
    class function Exists(const Path: string): Boolean; static;
    { a missing file is not an error }
    class procedure Delete(const Path: string); static;
    class procedure Copy(const SourceFileName, DestFileName: string; Overwrite: Boolean = False); static;
    { also to another volume; never replaces an existing file }
    class procedure Move(const SourceFileName, DestFileName: string); static;
    class function ReadAllText(const Path: string; Encoding: TEncoding = nil): string; static;
    class procedure WriteAllText(const Path, Contents: string; Encoding: TEncoding = nil); static;
    class function ReadAllBytes(const Path: string): TBytes; static;
    class procedure WriteAllBytes(const Path: string; const Bytes: TBytes); static;
    { a line ends with CR LF, LF or CR; the text after the last line end is a line unless it is empty }
    class function ReadAllLines(const Path: string; Encoding: TEncoding = nil): TStringDynArray; static;
    { CR LF after every line }
    class procedure WriteAllLines(const Path: string; const Lines: TStringDynArray; Encoding: TEncoding = nil); static;
    { creates a missing file; the byte order mark of a given Encoding only into an empty file }
    class procedure AppendAllText(const Path, Contents: string; Encoding: TEncoding = nil); static;
    class function GetSize(const Path: string): Int64; static;
    { local time }
    class function GetLastWriteTime(const Path: string): TDateTime; static;
    { the caller frees the stream. OpenRead: reading, others may read. OpenWrite: writing from the start of the
      existing content, a missing file is created. Create: a new or emptied file }
    class function OpenRead(const Path: string): TFileStream; static;
    class function OpenWrite(const Path: string): TFileStream; static;
    class function Create(const Path: string): TFileStream; static;
  end;

  { lists hold Path + separator + name (Path as given), in the order of the file system: the entries of a
    directory before those of its subdirectories; the pattern ('*' and '?') applies to the names listed, never to
    the directories searched; links to directories are listed but not entered }
  TDirectory = record
  public
    class function Exists(const Path: string): Boolean; static;
    { with every missing level; an existing directory is not an error }
    class procedure CreateDirectory(const Path: string); static;
    { Recursive = False: the directory must be empty }
    class procedure Delete(const Path: string; Recursive: Boolean = False); static;
    class function GetFiles(const Path: string; const SearchPattern: string = '*';
      SearchOption: TSearchOption = soTopDirectoryOnly): TStringDynArray; static;
    class function GetDirectories(const Path: string; const SearchPattern: string = '*';
      SearchOption: TSearchOption = soTopDirectoryOnly): TStringDynArray; static;
    class function GetCurrentDirectory: string; static;
    class procedure SetCurrentDirectory(const Path: string); static;
    { on the same volume; never replaces }
    class procedure Move(const SourceDirName, DestDirName: string); static;
  end;

implementation

uses
  RTLConsts;

var
  TempCounter: Cardinal;

function IsSep(C: Char): Boolean;
begin
  Result := (C = '\') or (C = '/');
end;

procedure RaiseIO(Code: Cardinal);
var
  E: EInOutError;
begin
  E := EInOutError.Create(SysErrorMessage(Code));
  E.ErrorCode := Integer(Code);
  raise E;
end;

{ the error Code of a failed operation on the file Path }
procedure RaiseFileError(Code: Cardinal; const Path: string);
begin
  if Code = ERROR_FILE_NOT_FOUND then
    raise EFileNotFoundException.CreateFmt(SIOFileNotFound, [Path]);
  if Code = ERROR_PATH_NOT_FOUND then
    raise EDirectoryNotFoundException.CreateFmt(SIODirectoryNotFound, [ExtractFileDir(Path)]);
  RaiseIO(Code);
end;

{ the error Code of a failed operation on the directory Path }
procedure RaiseDirError(Code: Cardinal; const Path: string);
begin
  if (Code = ERROR_FILE_NOT_FOUND) or (Code = ERROR_PATH_NOT_FOUND) then
    raise EDirectoryNotFoundException.CreateFmt(SIODirectoryNotFound, [Path]);
  RaiseIO(Code);
end;

function OpenForRead(const Path: string): THandle;
var
  Code: Cardinal;
begin
  Result := FileOpen(Path, fmOpenRead or fmShareDenyWrite);
  if Result = INVALID_HANDLE_VALUE then
  begin
    Code := Windows.GetLastError;
    RaiseFileError(Code, Path);
  end;
end;

{ Append = False: a new or emptied file. Append = True: the existing file positioned at its end, or a new one.
  Empty reports whether the file has no content }
function OpenForWrite(const Path: string; Append: Boolean; out Empty: Boolean): THandle;
var
  Code: Cardinal;
begin
  Empty := True;
  if Append and FileExists(Path) then
    Result := FileOpen(Path, fmOpenWrite or fmShareDenyWrite)
  else
    Result := FileCreate(Path);
  if Result = INVALID_HANDLE_VALUE then
  begin
    Code := Windows.GetLastError;
    RaiseFileError(Code, Path);
  end;
  if Append then
    Empty := FileSeek(Result, 0, 2) = 0;
end;

procedure WriteHandle(H: THandle; const Bytes: TBytes);
var
  Code: Cardinal;
begin
  if Length(Bytes) = 0 then
    Exit;
  if FileWrite(H, Bytes[0], Cardinal(Length(Bytes))) <> Length(Bytes) then
  begin
    Code := Windows.GetLastError;
    RaiseIO(Code);
  end;
end;

procedure WriteText(const Path, Contents: string; Encoding: TEncoding; Append: Boolean);
var
  H: THandle;
  Empty: Boolean;
begin
  H := OpenForWrite(Path, Append, Empty);
  try
    if Encoding = nil then
      WriteHandle(H, TEncoding.UTF8.GetBytes(Contents))
    else
    begin
      if Empty then
        WriteHandle(H, Encoding.GetPreamble);
      WriteHandle(H, Encoding.GetBytes(Contents));
    end;
  finally
    FileClose(H);
  end;
end;

{ the entries of Dir whose name matches Pattern: directories (Dirs) or files; with Recurse also those of the
  subdirectories }
procedure Collect(const Dir, Pattern: string; Dirs, Recurse: Boolean; var List: TStringDynArray; var Count: Integer);
var
  F: TSearchRec;
begin
  if FindFirst(TPath.Combine(Dir, Pattern), faAnyFile, F) = 0 then
    try
      repeat
        if ((F.Attr and faDirectory <> 0) = Dirs) and (F.Name <> '.') and (F.Name <> '..') then
        begin
          if Count = Length(List) then
            SetLength(List, Count * 2 + 16);
          List[Count] := TPath.Combine(Dir, F.Name);
          Count := Count + 1;
        end;
      until FindNext(F) <> 0;
    finally
      FindClose(F);
    end;
  if Recurse and (FindFirst(TPath.Combine(Dir, '*'), faAnyFile, F) = 0) then
    try
      repeat
        if (F.Attr and faDirectory <> 0) and (F.Attr and faSymLink = 0) and (F.Name <> '.') and (F.Name <> '..') then
          Collect(TPath.Combine(Dir, F.Name), Pattern, Dirs, True, List, Count);
      until FindNext(F) <> 0;
    finally
      FindClose(F);
    end;
end;

function List(const Path, Pattern: string; Dirs: Boolean; Option: TSearchOption): TStringDynArray;
var
  Count: Integer;
begin
  if not DirectoryExists(Path) then
    raise EDirectoryNotFoundException.CreateFmt(SIODirectoryNotFound, [Path]);
  Result := nil;
  Count := 0;
  Collect(Path, Pattern, Dirs, Option = soAllDirectories, Result, Count);
  SetLength(Result, Count);
end;

procedure RemoveTree(const Path: string);
var
  F: TSearchRec;
  Name: string;
  Code: Cardinal;
begin
  if FindFirst(TPath.Combine(Path, '*'), faAnyFile, F) = 0 then
    try
      repeat
        if (F.Name <> '.') and (F.Name <> '..') then
        begin
          Name := TPath.Combine(Path, F.Name);
          if F.Attr and faDirectory = 0 then
          begin
            if not DeleteFileW(PChar(Name)) then
            begin
              Code := Windows.GetLastError;
              RaiseIO(Code);
            end;
          end
          else if F.Attr and faSymLink = 0 then
            RemoveTree(Name)
          else if not RemoveDirectoryW(PChar(Name)) then
          begin
            Code := Windows.GetLastError;
            RaiseIO(Code);
          end;
        end;
      until FindNext(F) <> 0;
    finally
      FindClose(F);
    end;
  if not RemoveDirectoryW(PChar(Path)) then
  begin
    Code := Windows.GetLastError;
    RaiseDirError(Code, Path);
  end;
end;

{ ------------------------------------------------------------------------------------------------------ TPath }

class function TPath.Combine(const P1, P2: string): string;
begin
  if P2 = '' then
    Result := P1
  else if (P1 = '') or IsPathRooted(P2) then
    Result := P2
  else if IsSep(P1[Length(P1)]) or (P1[Length(P1)] = ':') then
    Result := P1 + P2
  else
    Result := P1 + DirectorySeparatorChar + P2;
end;

class function TPath.GetFileName(const Path: string): string;
begin
  Result := ExtractFileName(Path);
end;

class function TPath.GetFileNameWithoutExtension(const Path: string): string;
begin
  Result := ChangeFileExt(ExtractFileName(Path), '');
end;

class function TPath.GetExtension(const Path: string): string;
begin
  Result := ExtractFileExt(Path);
end;

class function TPath.GetDirectoryName(const Path: string): string;
begin
  Result := ExtractFileDir(Path);
end;

class function TPath.ChangeExtension(const Path, Extension: string): string;
begin
  if (Extension <> '') and (Extension[1] <> '.') then
    Result := ChangeFileExt(Path, '.' + Extension)
  else
    Result := ChangeFileExt(Path, Extension);
end;

class function TPath.GetFullPath(const Path: string): string;
begin
  Result := ExpandFileName(Path);
end;

class function TPath.GetTempPath: string;
begin
  Result := GetTempDir;
end;

class function TPath.GetTempFileName: string;
var
  Seed, Code: Cardinal;
  H: THandle;
begin
  TempCounter := (TempCounter + 1) and $FFFF;
  Seed := GetTickCount xor (GetCurrentProcessId shl 16) xor TempCounter;
  repeat
    Result := GetTempDir + 'tmp' + IntToHex(Seed, 8) + '.tmp';
    H := CreateFileW(PChar(Result), GENERIC_WRITE, 0, nil, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, 0);
    if H <> INVALID_HANDLE_VALUE then
      Break;
    Code := Windows.GetLastError;
    if Code <> ERROR_FILE_EXISTS then
      RaiseFileError(Code, Result);
    Seed := (Seed + 1) and $FFFFFFFF;
  until False;
  CloseHandle(H);
end;

class function TPath.HasExtension(const Path: string): Boolean;
begin
  Result := Length(ExtractFileExt(Path)) > 1;
end;

class function TPath.IsPathRooted(const Path: string): Boolean;
begin
  Result := (Path <> '') and (IsSep(Path[1]) or ((Length(Path) >= 2) and (Path[2] = ':')));
end;

class function TPath.GetHomePath: string;
begin
  Result := ExcludeTrailingPathDelimiter(GetEnvironmentVariable('APPDATA'));
end;

class function TPath.GetDocumentsPath: string;
begin
  Result := Combine(GetEnvironmentVariable('USERPROFILE'), 'Documents');
end;

{ ------------------------------------------------------------------------------------------------------ TFile }

class function TFile.Exists(const Path: string): Boolean;
begin
  Result := FileExists(Path);
end;

class procedure TFile.Delete(const Path: string);
var
  Code: Cardinal;
begin
  if not DeleteFileW(PChar(Path)) then
  begin
    Code := Windows.GetLastError;
    if (Code <> ERROR_FILE_NOT_FOUND) and (Code <> ERROR_PATH_NOT_FOUND) then
      RaiseIO(Code);
  end;
end;

{ the error Code of a failed copy or move: a missing directory is the one of the source unless the source exists }
procedure RaiseTransferError(Code: Cardinal; const Source, Dest: string);
begin
  if (Code = ERROR_PATH_NOT_FOUND) and FileExists(Source) then
    RaiseFileError(Code, Dest)
  else
    RaiseFileError(Code, Source);
end;

class procedure TFile.Copy(const SourceFileName, DestFileName: string; Overwrite: Boolean);
var
  Code: Cardinal;
begin
  if not CopyFileW(PChar(SourceFileName), PChar(DestFileName), not Overwrite) then
  begin
    Code := Windows.GetLastError;
    RaiseTransferError(Code, SourceFileName, DestFileName);
  end;
end;

class procedure TFile.Move(const SourceFileName, DestFileName: string);
var
  Code: Cardinal;
begin
  if not MoveFileExW(PChar(SourceFileName), PChar(DestFileName), MOVEFILE_COPY_ALLOWED) then
  begin
    Code := Windows.GetLastError;
    RaiseTransferError(Code, SourceFileName, DestFileName);
  end;
end;

class function TFile.ReadAllBytes(const Path: string): TBytes;
var
  H: THandle;
  Size: Int64;
  Got: Integer;
  Code: Cardinal;
begin
  Result := nil;
  H := OpenForRead(Path);
  try
    Size := FileSeek(H, 0, 2);
    FileSeek(H, 0, 0);
    if Size > 0 then
    begin
      SetLength(Result, Integer(Size));
      Got := FileRead(H, Result[0], Cardinal(Size));
      if Got < 0 then
      begin
        Code := Windows.GetLastError;
        RaiseIO(Code);
      end;
      SetLength(Result, Got);
    end;
  finally
    FileClose(H);
  end;
end;

class procedure TFile.WriteAllBytes(const Path: string; const Bytes: TBytes);
var
  H: THandle;
  Empty: Boolean;
begin
  H := OpenForWrite(Path, False, Empty);
  try
    WriteHandle(H, Bytes);
  finally
    FileClose(H);
  end;
end;

class function TFile.ReadAllText(const Path: string; Encoding: TEncoding): string;
var
  Bytes: TBytes;
  Skip: Integer;
begin
  Bytes := ReadAllBytes(Path);
  Skip := TEncoding.GetBufferEncoding(Bytes, Encoding, TEncoding.UTF8);
  Result := Encoding.GetString(Bytes, Skip, Length(Bytes) - Skip);
end;

class procedure TFile.WriteAllText(const Path, Contents: string; Encoding: TEncoding);
begin
  WriteText(Path, Contents, Encoding, False);
end;

class function TFile.ReadAllLines(const Path: string; Encoding: TEncoding): TStringDynArray;
var
  S: string;
  I, Start, Count: Integer;
begin
  S := ReadAllText(Path, Encoding);
  Result := nil;
  Count := 0;
  Start := 1;
  I := 1;
  while Start <= Length(S) do
  begin
    while (I <= Length(S)) and (S[I] <> #13) and (S[I] <> #10) do
      I := I + 1;
    if Count = Length(Result) then
      SetLength(Result, Count * 2 + 16);
    Result[Count] := System.Copy(S, Start, I - Start);
    Count := Count + 1;
    if (I < Length(S)) and (S[I] = #13) and (S[I + 1] = #10) then
      I := I + 1;
    I := I + 1;
    Start := I;
  end;
  SetLength(Result, Count);
end;

class procedure TFile.WriteAllLines(const Path: string; const Lines: TStringDynArray; Encoding: TEncoding);
var
  B: TStringBuilder;
  I: Integer;
begin
  B := TStringBuilder.Create;
  try
    for I := 0 to Length(Lines) - 1 do
    begin
      B.Append(Lines[I]);
      B.Append(#13#10);
    end;
    WriteText(Path, B.ToString, Encoding, False);
  finally
    B.Free;
  end;
end;

class procedure TFile.AppendAllText(const Path, Contents: string; Encoding: TEncoding);
begin
  WriteText(Path, Contents, Encoding, True);
end;

{ the attribute data of the file Path; a directory counts as missing }
procedure FileData(const Path: string; out Data: TWin32FileAttributeData);
var
  Code: Cardinal;
begin
  if not GetFileAttributesExW(PChar(Path), GetFileExInfoStandard, @Data) then
  begin
    Code := Windows.GetLastError;
    RaiseFileError(Code, Path);
  end;
  if Data.dwFileAttributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
    RaiseFileError(ERROR_FILE_NOT_FOUND, Path);
end;

class function TFile.GetSize(const Path: string): Int64;
var
  Data: TWin32FileAttributeData;
begin
  FileData(Path, Data);
  Result := (Int64(Data.nFileSizeHigh) shl 32) or Int64(Data.nFileSizeLow);
end;

class function TFile.GetLastWriteTime(const Path: string): TDateTime;
var
  Data: TWin32FileAttributeData;
begin
  FileData(Path, Data);
  if not FileAge(Path, Result) then
    RaiseFileError(Windows.GetLastError, Path);
end;

{ a stream on Path; a missing file or directory raises the IOUtils classes, not the stream's }
function OpenStream(const Path: string; Mode: Word): TFileStream;
var
  Code: Cardinal;
begin
  try
    Result := TFileStream.Create(Path, Mode);
  except
    on EStreamError do
    begin
      Code := Windows.GetLastError;
      if (Code = ERROR_FILE_NOT_FOUND) or (Code = ERROR_PATH_NOT_FOUND) then
        RaiseFileError(Code, Path);
      raise;
    end;
  end;
end;

class function TFile.OpenRead(const Path: string): TFileStream;
begin
  Result := OpenStream(Path, fmOpenRead or fmShareDenyWrite);
end;

class function TFile.OpenWrite(const Path: string): TFileStream;
begin
  if FileExists(Path) then
    Result := OpenStream(Path, fmOpenWrite or fmShareDenyWrite)
  else
    Result := OpenStream(Path, fmCreate);
end;

class function TFile.Create(const Path: string): TFileStream;
begin
  Result := OpenStream(Path, fmCreate);
end;

{ ------------------------------------------------------------------------------------------------- TDirectory }

class function TDirectory.Exists(const Path: string): Boolean;
begin
  Result := DirectoryExists(Path);
end;

class procedure TDirectory.CreateDirectory(const Path: string);
var
  Dir, Parent: string;
  Code: Cardinal;
begin
  if DirectoryExists(Path) then
    Exit;
  Dir := ExcludeTrailingPathDelimiter(Path);
  Parent := ExtractFileDir(Dir);
  if (Parent <> '') and (Length(Parent) < Length(Dir)) and not DirectoryExists(Parent) then
    CreateDirectory(Parent);
  if not CreateDirectoryW(PChar(Dir), nil) then
  begin
    Code := Windows.GetLastError;
    if not DirectoryExists(Dir) then
      RaiseDirError(Code, Dir);
  end;
end;

class procedure TDirectory.Delete(const Path: string; Recursive: Boolean);
var
  Code: Cardinal;
begin
  if Recursive then
  begin
    if not DirectoryExists(Path) then
      raise EDirectoryNotFoundException.CreateFmt(SIODirectoryNotFound, [Path]);
    RemoveTree(Path);
  end
  else if not RemoveDirectoryW(PChar(Path)) then
  begin
    Code := Windows.GetLastError;
    RaiseDirError(Code, Path);
  end;
end;

class function TDirectory.GetFiles(const Path: string; const SearchPattern: string;
  SearchOption: TSearchOption): TStringDynArray;
begin
  Result := List(Path, SearchPattern, False, SearchOption);
end;

class function TDirectory.GetDirectories(const Path: string; const SearchPattern: string;
  SearchOption: TSearchOption): TStringDynArray;
begin
  Result := List(Path, SearchPattern, True, SearchOption);
end;

class function TDirectory.GetCurrentDirectory: string;
begin
  Result := GetCurrentDir;
end;

class procedure TDirectory.SetCurrentDirectory(const Path: string);
var
  Code: Cardinal;
begin
  if not SetCurrentDirectoryW(PChar(Path)) then
  begin
    Code := Windows.GetLastError;
    RaiseDirError(Code, Path);
  end;
end;

class procedure TDirectory.Move(const SourceDirName, DestDirName: string);
var
  Code: Cardinal;
begin
  if not MoveFileExW(PChar(SourceDirName), PChar(DestDirName), 0) then
  begin
    Code := Windows.GetLastError;
    if not DirectoryExists(SourceDirName) then
      RaiseDirError(ERROR_PATH_NOT_FOUND, SourceDirName);
    RaiseDirError(Code, DestDirName);
  end;
end;

end.
