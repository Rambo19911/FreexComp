// covers: UIF-1306
// needs: desktop
// phase: 8
program Perf_RichEditLoad;
{ U-146, rich-edit.md section 9 (card F05-038 acceptance 4): writes a text file of 5 MB of 60-character lines into
  the directory given as the first argument, loads it with PlainText = True into a visible rich edit five times
  after one warm-up, and prints the number of characters the control holds and the median of the five loads,
  measured as CPU time of the thread. The bound is checked by RichEditPerfTests.LoadFiveMegabytesOfText; a load is
  one EM_SETTEXTEX (UIF-1306), never a loop over the lines. }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, StdCtrls, ComCtrls;

const
  LineLength = 60;
  FileSize = 5 * 1024 * 1024;
  Loads = 5;

type
  TGetThreadTimes = function(hThread: THandle; var lpCreationTime, lpExitTime, lpKernelTime,
    lpUserTime: TFileTime): BOOL; stdcall;

var
  { the Windows unit has no binding for GetThreadTimes: the function is looked up in kernel32 by name }
  GetThreadTimes: TGetThreadTimes;

{ CPU time of the calling thread (kernel + user) in units of 100 ns }
function ThreadCpu: Int64;
var
  Created, Exited, Kernel, User: TFileTime;
begin
  if not Assigned(GetThreadTimes) then
    GetThreadTimes := TGetThreadTimes(GetProcAddress(GetModuleHandleW(kernel32), 'GetThreadTimes'));
  GetThreadTimes(GetCurrentThread, Created, Exited, Kernel, User);
  Result := (Int64(Kernel.dwHighDateTime) shl 32 or Kernel.dwLowDateTime)
    + (Int64(User.dwHighDateTime) shl 32 or User.dwLowDateTime);
end;

{ lines of LineLength characters, each followed by CR LF, up to FileSize bytes; returns the number of lines }
function WriteSample(const FileName: string): Integer;
var
  S: TFileStream;
  Line: array[0..LineLength + 1] of Byte;
  I: Integer;
begin
  for I := 0 to LineLength - 1 do
    Line[I] := Ord('a') + I mod 26;
  Line[LineLength] := 13;
  Line[LineLength + 1] := 10;
  Result := FileSize div (LineLength + 2);
  S := TFileStream.Create(FileName, fmCreate);
  try
    for I := 1 to Result do
      S.WriteBuffer(Line, LineLength + 2);
  finally
    S.Free;
  end;
end;

var
  F: TForm;
  R: TRichEdit;
  FileName: string;
  Times: array[0..Loads - 1] of Int64;
  I, J, Lines: Integer;
  T: Int64;
begin
  Application.Initialize;
  FileName := IncludeTrailingPathDelimiter(ParamStr(1)) + 'five-megabytes.txt';
  Lines := WriteSample(FileName);
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 640;
    F.ClientHeight := 480;
    R := TRichEdit.Create(F);
    R.Parent := F;
    R.Align := alClient;
    R.ScrollBars := ssVertical;
    R.PlainText := True;
    F.Show;
    Application.ProcessMessages;
    R.Lines.LoadFromFile(FileName);                          { warm-up }
    Application.ProcessMessages;
    for I := 0 to Loads - 1 do
    begin
      R.Lines.Clear;
      Application.ProcessMessages;
      T := ThreadCpu;
      R.Lines.LoadFromFile(FileName);
      Times[I] := ThreadCpu - T;
      Application.ProcessMessages;
    end;
    for I := 1 to Loads - 1 do
      for J := I downto 1 do
        if Times[J] < Times[J - 1] then
        begin
          T := Times[J];
          Times[J] := Times[J - 1];
          Times[J - 1] := T;
        end;
    Writeln('lines=', Lines);
    Writeln('chars=', Length(R.Text));
    Writeln('ms=', Times[Loads div 2] div 10000);
  finally
    F.Free;
    DeleteFile(FileName);
  end;
end.
