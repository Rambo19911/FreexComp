// covers: RTL-113, RTL-120, RTL-121, RTL-122, RTL-124, RTL-125
// opc-run: tempdir
program Sys_TextFile;
{ Disk text files in the temporary directory passed as the first argument: write, append and read back; UTF-8 without
  a BOM on Rewrite, a UTF-8 BOM skipped on Reset, UTF-16LE with SetTextCodePage(1200); Flush; disk files are buffered
  (not flushed at the end of a statement); Reset of an open file closes (and flushes) it first; Erase and Rename; the
  aliases Assign and Close; an empty name binds Reset to standard input and Rewrite to standard output; a line of
  multi-byte sequences longer than the read block. The raw bytes are checked with our own kernel32 calls. }
{$POINTERMATH ON}

const
  GENERIC_READ = $80000000;
  GENERIC_WRITE = $40000000;
  FILE_SHARE_READ = 1;
  FILE_SHARE_WRITE = 2;
  CREATE_ALWAYS = 2;
  OPEN_EXISTING = 3;
  INVALID_HANDLE_VALUE = $FFFFFFFFFFFFFFFF;

function CreateFileW(lpFileName: PWideChar; dwDesiredAccess, dwShareMode: Cardinal; lpSecurityAttributes: Pointer;
  dwCreationDisposition, dwFlagsAndAttributes: Cardinal; hTemplateFile: THandle): THandle; stdcall;
  external 'kernel32.dll' name 'CreateFileW';
function ReadFile(hFile: THandle; lpBuffer: Pointer; nNumberOfBytesToRead: Cardinal; var lpNumberOfBytesRead: Cardinal;
  lpOverlapped: Pointer): LongBool; stdcall; external 'kernel32.dll' name 'ReadFile';
function WriteFile(hFile: THandle; lpBuffer: Pointer; nNumberOfBytesToWrite: Cardinal; var lpNumberOfBytesWritten: Cardinal;
  lpOverlapped: Pointer): LongBool; stdcall; external 'kernel32.dll' name 'WriteFile';
function CloseHandle(hObject: THandle): LongBool; stdcall; external 'kernel32.dll' name 'CloseHandle';

var
  Dir, S: string;
  F, G: Text;
  C: Char;
  I, K: Integer;
  Ok: Boolean;
  Bom: array[0..5] of Byte;
  Written: Cardinal;
  H: THandle;

procedure WriteHex(B: Integer);
const
  Digits = '0123456789ABCDEF';
begin
  Write(' ', Digits[B div 16 + 1], Digits[B mod 16 + 1]);
end;

{ the byte count (at most 32) and the first bytes of a file, read while other handles may write it }
procedure Dump(const Name: string);
var
  H: THandle;
  Buf: array[0..31] of Byte;
  Got: Cardinal;
  I: Integer;
begin
  Write(Name, ':');
  H := CreateFileW(PChar(Dir + '\' + Name), GENERIC_READ, FILE_SHARE_READ or FILE_SHARE_WRITE, nil, OPEN_EXISTING, 0, 0);
  if H = INVALID_HANDLE_VALUE then
  begin
    Writeln(' missing');
    Exit;
  end;
  Got := 0;
  ReadFile(H, @Buf[0], 32, Got, nil);
  CloseHandle(H);
  Write(' ', Got);
  for I := 0 to Integer(Got) - 1 do
    WriteHex(Buf[I]);
  Writeln;
end;

begin
  Dir := ParamStr(1);                     { the runner passes the directory as the first argument }

  AssignFile(F, Dir + '\a.txt');
  Rewrite(F);
  Write(F, 'Ā ', 42);
  Dump('a.txt');                           { still in the buffer: disk files are not flushed per statement }
  Flush(F);
  Dump('a.txt');                           { UTF-8, no BOM }
  Writeln(F, ' ', 2.5:0:1);
  CloseFile(F);
  Dump('a.txt');
  Append(F);
  Writeln(F, 'appended');
  Close(F);
  Reset(F);
  while not Eof(F) do
  begin
    Readln(F, S);
    Writeln('[', S, ']');
  end;
  CloseFile(F);

  Rewrite(F);                              { CREATE_ALWAYS truncates }
  Write(F, 'xy');
  Reset(F);                                { closes and flushes first }
  Read(F, C);
  Writeln(C, ' ', Eof(F), ' ', Eoln(F));
  Read(F, C);
  Writeln(C, ' ', Eof(F), ' ', Eoln(F));
  CloseFile(F);

  H := CreateFileW(PChar(Dir + '\bom.txt'), GENERIC_WRITE, 0, nil, CREATE_ALWAYS, 0, 0);
  Bom[0] := $EF;
  Bom[1] := $BB;
  Bom[2] := $BF;
  Bom[3] := $35;                           { '5' }
  Bom[4] := $0A;
  WriteFile(H, @Bom[0], 5, Written, nil);
  CloseHandle(H);
  Assign(G, Dir + '\bom.txt');
  Reset(G);
  Read(G, C);
  Writeln('bom skipped ', C, ' ', Eoln(G));
  Close(G);

  AssignFile(G, Dir + '\w.txt');
  SetTextCodePage(G, 1200);
  Rewrite(G);
  Writeln(G, 'hé'#$D83D#$DE00);
  CloseFile(G);
  Dump('w.txt');                           { BOM FF FE, UTF-16LE }
  Reset(G);
  Readln(G, S);
  Writeln(Length(S), ' ', S = 'hé'#$D83D#$DE00, ' ', Eof(G));
  CloseFile(G);

  Rewrite(F);
  for I := 1 to 3000 do
    Write(F, #$20AC);
  for I := 1 to 2500 do
    Write(F, #$D83D#$DE00);
  Writeln(F);
  Writeln(F, 'end');
  CloseFile(F);
  Reset(F);
  Readln(F, S);
  Ok := Length(S) = 8000;
  for K := 1 to 3000 do
    if S[K] <> #$20AC then
      Ok := False;
  K := 3001;
  while K < Length(S) do
  begin
    if (S[K] <> #$D83D) or (S[K + 1] <> #$DE00) then
      Ok := False;
    K := K + 2;
  end;
  Readln(F, S);
  Writeln('long line ', Ok, ' ', S, ' ', Eof(F));
  CloseFile(F);

  Rename(F, Dir + '\b.txt');
  Dump('a.txt');
  Reset(F);                                { F now names b.txt }
  Readln(F, S);
  Writeln('renamed ', Length(S));
  CloseFile(F);
  Erase(F);
  Dump('b.txt');

  AssignFile(G, '');
  Rewrite(G);                              { standard output }
  Writeln(G, 'to standard output');
  CloseFile(G);
  Reset(G);                                { standard input }
  Readln(G, S);
  Writeln('[', S, '] ', Eof(G));
  CloseFile(G);
  Writeln('done');
end.
