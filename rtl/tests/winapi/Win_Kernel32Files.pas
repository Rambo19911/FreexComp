// covers: RTL-820, RTL-821, RTL-860
// opc-run: tempdir
program Win_Kernel32Files;
{ Windows unit, kernel32 file functions (winapi.md §5.1 "Files") inside the fresh folder ParamStr(1) (RTL-903). }
uses Windows, TestKit;

var
  Dir, Sub, A, B, C: string;
  H: THandle;
  N: DWORD;
  Size, Position: Int64;
  Buffer: array[0..MAX_PATH] of WideChar;
  Bytes: array[0..15] of Byte;
  FilePart: PWideChar;
  Data: TWin32FileAttributeData;
  Find: TWin32FindDataW;
  Count: Integer;

begin
  Dir := ParamStr(1);
  Sub := Dir + '\sub';
  A := Sub + '\a.txt';
  B := Sub + '\b.txt';
  C := Sub + '\c.txt';

  Section('directories');
  Check(CreateDirectoryW(PWideChar(Sub), nil), 'CreateDirectoryW');
  CheckEqB(False, CreateDirectory(PWideChar(Sub), nil), 'CreateDirectory alias, exists');
  CheckEqI(ERROR_ALREADY_EXISTS, GetLastError, 'ERROR_ALREADY_EXISTS');
  Check(SetCurrentDirectoryW(PWideChar(Sub)), 'SetCurrentDirectoryW');
  N := GetCurrentDirectoryW(MAX_PATH, @Buffer[0]);
  Check((N > 4) and (Buffer[N - 1] = 'b') and (Buffer[N - 2] = 'u') and (Buffer[N - 3] = 's'), 'GetCurrentDirectoryW');
  Check(GetTempPathW(MAX_PATH, @Buffer[0]) > 0, 'GetTempPathW');
  N := GetFullPathNameW('a.txt', MAX_PATH, @Buffer[0], @FilePart);
  CheckEqS(A, string(PWideChar(@Buffer[0])), 'GetFullPathNameW');
  CheckEqS('a.txt', string(FilePart), 'GetFullPathNameW file part');

  Section('handles');
  H := CreateFileW(PWideChar(A), GENERIC_READ or GENERIC_WRITE, FILE_SHARE_READ, nil, CREATE_NEW,
    FILE_ATTRIBUTE_NORMAL, 0);
  Check(H <> INVALID_HANDLE_VALUE, 'CreateFileW');
  CheckEqI(FILE_TYPE_DISK, GetFileType(H), 'GetFileType disk');
  Bytes[0] := 1; Bytes[1] := 2; Bytes[2] := 3; Bytes[3] := 4; Bytes[4] := 5;
  Check(WriteFile(H, @Bytes[0], 5, N, nil) and (N = 5), 'WriteFile');
  Check(FlushFileBuffers(H), 'FlushFileBuffers');
  Check(GetFileSizeEx(H, Size) and (Size = 5), 'GetFileSizeEx');
  Check(SetFilePointerEx(H, -3, @Position, FILE_END) and (Position = 2), 'SetFilePointerEx');
  Bytes[0] := 0;
  Check(ReadFile(H, @Bytes[0], 16, N, nil) and (N = 3) and (Bytes[0] = 3), 'ReadFile');
  Check(SetFilePointerEx(H, 4, nil, FILE_BEGIN) and SetEndOfFile(H), 'SetEndOfFile');
  Check(GetFileSizeEx(H, Size) and (Size = 4), 'size after SetEndOfFile');
  Check(CloseHandle(H), 'CloseHandle');
  H := CreateFile(PWideChar(A), GENERIC_READ, 0, nil, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, 0);
  Check(H = INVALID_HANDLE_VALUE, 'CreateFile alias, CREATE_NEW on existing file');
  CheckEqI(ERROR_FILE_EXISTS, GetLastError, 'ERROR_FILE_EXISTS');

  Section('attributes');
  Check(SetFileAttributesW(PWideChar(A), FILE_ATTRIBUTE_READONLY), 'SetFileAttributesW');
  CheckEqI(FILE_ATTRIBUTE_READONLY, GetFileAttributesW(PWideChar(A)) and FILE_ATTRIBUTE_READONLY, 'GetFileAttributesW');
  CheckEqB(False, DeleteFileW(PWideChar(A)), 'DeleteFileW on a read-only file');
  CheckEqI(ERROR_ACCESS_DENIED, GetLastError, 'ERROR_ACCESS_DENIED');
  Check(SetFileAttributes(PWideChar(A), FILE_ATTRIBUTE_NORMAL), 'SetFileAttributes alias');
  Check(GetFileAttributesExW(PWideChar(A), GetFileExInfoStandard, @Data), 'GetFileAttributesExW');
  Check((Data.nFileSizeLow = 4) and (Data.nFileSizeHigh = 0) and (Data.ftLastWriteTime.dwHighDateTime > 0),
    'GetFileAttributesExW data');
  CheckEqI(FILE_ATTRIBUTE_DIRECTORY, GetFileAttributes(PWideChar(Sub)) and FILE_ATTRIBUTE_DIRECTORY, 'directory attribute');
  CheckEqI(INVALID_FILE_ATTRIBUTES, GetFileAttributesW(PWideChar(Sub + '\none')), 'INVALID_FILE_ATTRIBUTES');
  CheckEqI(ERROR_FILE_NOT_FOUND, GetLastError, 'ERROR_FILE_NOT_FOUND');

  Section('copy, move, find, delete');
  Check(CopyFileW(PWideChar(A), PWideChar(B), True), 'CopyFileW');
  CheckEqB(False, CopyFileW(PWideChar(A), PWideChar(B), True), 'CopyFileW fail if exists');
  Check(MoveFileW(PWideChar(B), PWideChar(C)), 'MoveFileW');
  Check(CopyFileW(PWideChar(A), PWideChar(B), False), 'CopyFileW again');
  CheckEqB(False, MoveFileExW(PWideChar(B), PWideChar(C), 0), 'MoveFileExW onto an existing file');
  Check(MoveFileExW(PWideChar(B), PWideChar(C), MOVEFILE_REPLACE_EXISTING), 'MoveFileExW replace');
  Count := 0;
  H := FindFirstFileW(PWideChar(Sub + '\*.txt'), Find);
  Check(H <> INVALID_HANDLE_VALUE, 'FindFirstFileW');
  repeat
    Count := Count + 1;
  until not FindNextFileW(H, Find);
  CheckEqI(ERROR_NO_MORE_FILES, GetLastError, 'ERROR_NO_MORE_FILES');
  Check(FindClose(H), 'FindClose');
  CheckEqI(2, Count, 'found a.txt and c.txt');
  Check(DeleteFileW(PWideChar(A)) and DeleteFile(PWideChar(C)), 'DeleteFileW');
  Check(SetCurrentDirectoryW(PWideChar(Dir)), 'leave the folder');
  Check(RemoveDirectoryW(PWideChar(Sub)), 'RemoveDirectoryW');
  CheckEqB(False, RemoveDirectory(PWideChar(Sub)), 'RemoveDirectory alias, gone');
  Done;
end.
