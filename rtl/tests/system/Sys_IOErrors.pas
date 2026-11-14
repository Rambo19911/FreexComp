// covers: RTL-094, RTL-114, RTL-121, RTL-123, RTL-124, ABI-132
// opc-run: tempdir
program Sys_IOErrors;
{ I/O errors in the temporary directory passed as the first argument. With $I- the program reads them with IOResult
  (which resets them): Win32 errors mapped by RTL-123 (2 missing file, 3 missing path, 5 access denied for a
  directory and for a sharing violation), 102 unassigned, 103 closed, 104 reading an output file, 105 writing an input
  file, 106 a malformed number and an unsupported code page; SetInOutRes and _IOCheck; a failed WriteFile gives 101,
  for a disk file and for Output (the handles are swapped for read-only ones through the private layout). Then with
  $I+ a failed Reset ends the program with runtime error 2. }
{$I ..\..\src\AbiLayout.inc}
{$I-}

const
  GENERIC_READ = $80000000;
  FILE_SHARE_READ = 1;
  FILE_SHARE_WRITE = 2;
  OPEN_EXISTING = 3;

function CreateFileW(lpFileName: PWideChar; dwDesiredAccess, dwShareMode: Cardinal; lpSecurityAttributes: Pointer;
  dwCreationDisposition, dwFlagsAndAttributes: Cardinal; hTemplateFile: THandle): THandle; stdcall;
  external 'kernel32.dll' name 'CreateFileW';
function CloseHandle(hObject: THandle): LongBool; stdcall; external 'kernel32.dll' name 'CloseHandle';

var
  Dir: string;
  F, G, U: Text;
  I: Integer;
  X: Double;
  S: string;
  H, Saved: THandle;

procedure Show(const What: string);
begin
  Writeln(What, ' ', IOResult);
end;

procedure FailingReset;
begin
{$I+}
  Reset(F);
  Write('');                               { the I/O check of this statement reports the error of Reset (ABI-132) }
{$I-}
end;

begin
  Dir := ParamStr(1);                     { the runner passes the directory as the first argument }
  Show('start');

  AssignFile(F, Dir + '\missing.txt');
  Reset(F);
  Show('missing file');
  Show('reset by IOResult');
  Append(F);
  Show('append missing file');
  AssignFile(F, Dir + '\no\such\dir\x.txt');
  Rewrite(F);
  Show('missing path');
  AssignFile(F, Dir);
  Reset(F);
  Show('directory');

  Reset(U);
  Show('unassigned');
  Writeln(U, 'x');
  Show('write unassigned');

  AssignFile(F, Dir + '\a.txt');
  Writeln(F, 'x');
  Show('write closed');
  CloseFile(F);
  Show('close closed');
  Flush(F);
  Show('flush closed');
  Rewrite(F);
  Writeln(F, '12 x 1e');
  Read(F, I);
  Show('read output file');
  AssignFile(G, Dir + '\a.txt');
  Rewrite(G);
  Show('sharing violation');
  CloseFile(F);
  Show('close');

  Reset(F);
  Writeln(F, 'y');
  Show('write input file');
  Flush(F);
  Show('flush input file');
  Read(F, I);
  Show('read ' + Chr(48 + I mod 10));
  Read(F, I);
  Writeln(I, ' ', IOResult);
  Read(F, X);                              { x }
  Writeln(X:0:1, ' ', IOResult);
  Read(F, X);                              { 1e }
  Writeln(X:0:1, ' ', IOResult);
  Readln(F, S);
  Show('readln');
  CloseFile(F);

  SetTextCodePage(F, 1252);
  Reset(F);
  Show('code page 1252');
  SetTextCodePage(F, 65001);

  SetInOutRes(77);
  Show('SetInOutRes');
  SetInOutRes(0);
  _IOCheck;                                { nothing to report }
  Show('_IOCheck with 0');

  Rewrite(F);
  H := CreateFileW(PChar(Dir + '\a.txt'), GENERIC_READ, FILE_SHARE_READ or FILE_SHARE_WRITE, nil, OPEN_EXISTING, 0, 0);
  Saved := PTextRec(@F)^.Handle;
  PTextRec(@F)^.Handle := H;
  Write(F, 'buffered');
  Show('write to read-only handle');
  Flush(F);
  Show('flush to read-only handle');
  PTextRec(@F)^.Handle := Saved;
  CloseFile(F);
  Show('close');
  Saved := PTextRec(@Output)^.Handle;
  PTextRec(@Output)^.Handle := H;
  Writeln('lost');
  PTextRec(@Output)^.Handle := Saved;
  Show('Output to read-only handle');
  CloseHandle(H);

  AssignFile(F, Dir + '\missing.txt');
  FailingReset;
  Writeln('not reached');
end.
