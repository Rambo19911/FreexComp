// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1394
// needs: desktop
// opc-run: rawargs --dump-layout dump.json
// phase: 8
program Frm_FrameDump;
{ U-154, application-forms.md "Frames" UIF-1394 with layout.md UIF-320...324 (a group A program in dump mode, as
  Dump_Mode: the corpus of group B reads .form files into a plain TForm and cannot hold the classes of a frame
  unit): the dump of a form with two instances of the frame of Frm_AddressFrame lists both instances with their
  class and name and, under each, the frame's children in Controls[] order - the override of the first instance
  (CityEdit's Width) and the instance geometry of the second (Height) included. The expected JSON (.out) is written
  by hand from the layout rules: stored bounds, no scaling (dpi 96), no auto-size measurement in dump mode. }
uses
  Windows, SysUtils, Classes, Controls, Forms, StdCtrls, Frm_AddressFrame;

type
  TDumpForm = class(TForm)
  published
    BillingAddress: TAddressFrame;
    ShippingAddress: TAddressFrame;
    OkButton: TButton;
  end;

{$FORM Frm_FrameDump.form}

var
  DumpForm: TDumpForm;

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
  Application.CreateForm(TDumpForm, DumpForm);
  Application.Run;
  WriteLn('Run returned');
end.
