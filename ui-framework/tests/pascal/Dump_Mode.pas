// covers: UIF-310, UIF-311, UIF-312, UIF-313, UIF-314, UIF-315
// needs: desktop
// opc-run: rawargs --dump-layout dump.json
program Dump_Mode;
{ U-050, layout.md section 13: --dump-layout <file> sets dump mode at start-up (Application.DumpLayoutFile,
  NonInteractive); PixelsPerInch is 96 and the stored PixelsPerInch = 192 is not scaled; OnCreate runs, OnShow and
  OnActivate do not; a window handle forced by OnCreate changes no bound (no OS feedback, no client adoption);
  Application.Run writes the dump of every form in creation order (visible or not) and halts with exit code 0
  without showing a form. An exit procedure prints the written file (and checks it has no BOM and no CR); the
  observations of OnCreate are recorded as names of controls so that they appear in the dump. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

type
  TBox = class(TCustomControl);

  TMainForm = class(TForm)
  published
    Box: TBox;
    Right: TBox;
    procedure FormCreate(Sender: TObject);
    procedure FormShow(Sender: TObject);
  end;

{$FORM Dump_Mode.form}

var
  MainForm: TMainForm;
  Second: TForm;

procedure Note(F: TForm; const AName: string);
var
  C: TBox;
begin
  C := TBox.Create(F);
  C.Name := AName;
  C.SetBounds(0, 0, 1, 1);
  C.Parent := F;
end;

procedure TMainForm.FormCreate(Sender: TObject);
begin
  if PixelsPerInch = 96 then
    Note(Self, 'Ppi96');
  if Application.NonInteractive then
    Note(Self, 'NonInteractive');
  if Application.DumpLayoutFile = 'dump.json' then
    Note(Self, 'DumpFileDetected');
  HandleNeeded;                                              { user code forces handles: bounds stay as computed }
  Box.HandleNeeded;
  ClientWidth := 320;                                        { a later change with a handle: no OS feedback either }
end;

procedure TMainForm.FormShow(Sender: TObject);
begin
  Note(Self, 'ShownOrActivated');
end;

procedure PrintDump;
var
  Stream: TFileStream;
  Bytes: TBytes;
  I: Integer;
  S: string;
begin
  if not FileExists('dump.json') then
  begin
    WriteLn('no dump file');
    Exit;
  end;
  Stream := TFileStream.Create('dump.json', fmOpenRead);
  try
    SetLength(Bytes, Stream.Size);
    if Length(Bytes) > 0 then
      Stream.ReadBuffer(Bytes[0], Length(Bytes));
  finally
    Stream.Free;
  end;
  if (Length(Bytes) >= 3) and (Bytes[0] = $EF) and (Bytes[1] = $BB) and (Bytes[2] = $BF) then
    WriteLn('BOM present');
  for I := 0 to Length(Bytes) - 1 do
    if Bytes[I] = 13 then
    begin
      WriteLn('CR present');
      Break;
    end;
  S := TEncoding.UTF8.GetString(Bytes);
  Write(S);
  WriteLn('exit code ', ExitCode);
end;

begin
  RegisterClass(TBox);
  AddExitProc(PrintDump);
  Application.Initialize;
  Application.CreateForm(TMainForm, MainForm);
  Application.CreateForm(TForm, Second);
  Second.Name := 'Second';
  Application.Run;
  WriteLn('Run returned');
end.
