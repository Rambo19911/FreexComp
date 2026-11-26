// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1452
// needs: desktop
// opc-run: rawargs --dump-layout dump.json
// phase: 8
program Frm_InheritedDump;
{ U-160, ui-framework/phase8/form-inheritance.md UIF-1452 with layout.md UIF-320...324 (a group A program in dump
  mode, as Frm_FrameDump: the corpus of group B reads .form files into a plain TForm and cannot hold a chain of form
  classes): the dump of the third level of the chain TBaseDialog (Frm_InheritedBase) -> TRenameDialog
  (Frm_InheritedMid) -> TDumpDialog (this program) lists, in every container, the inherited children first (the
  ancestors' order, base-most level first) and then the children each later level adds - the root (level 1's panel
  and page control, level 2's edit, level 3's button), the inherited ButtonPanel (level 1's two buttons, level 2's,
  level 3's) and the inherited AdvancedPage (level 3's button). Level 3 also moves OkButton. The expected JSON
  (.out) is written by hand from the layout rules: stored bounds, ButtonPanel aligned to the bottom of the 360 x 160
  client area, the tab sheets on the page rectangle of UIF-219 (TabH = 12 + 9 at 96 dpi), only the active page of
  level 2 visible (UIF-771), no scaling (dpi 96), no auto-size measurement in dump mode. }
uses
  Windows, SysUtils, Classes, Controls, Forms, StdCtrls, Frm_InheritedBase, Frm_InheritedMid;

type
  TDumpDialog = class(TRenameDialog)
  published
    ApplyButton: TButton;
    ResetButton: TButton;
    MoreButton: TButton;
  end;

{$FORM Frm_InheritedDump.form}

var
  DumpDialog: TDumpDialog;

procedure PrintDump;
var
  Stream: TFileStream;
  Bytes: TBytes;
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
  Write(TEncoding.UTF8.GetString(Bytes));
  WriteLn('exit code ', ExitCode);
end;

begin
  AddExitProc(PrintDump);
  Application.Initialize;
  Application.CreateForm(TDumpDialog, DumpDialog);
  Application.Run;
  WriteLn('Run returned');
end.
