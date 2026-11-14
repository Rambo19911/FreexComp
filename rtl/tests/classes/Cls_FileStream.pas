// phase: 4
// covers: RTL-430, RTL-431, RTL-432, RTL-433
// opc-run: tempdir
program Cls_FileStream;
{ TStream, THandleStream and TFileStream (classes.md section 6): create/write/reopen/read a file in the temp folder
  given as the first argument; Position and Size (seek to the end and back, RTL-432/433); ReadBuffer past the end
  raises EReadError "Stream read error" (RTL-430); SetSize truncates; CopyFrom with a count and with 0 (RTL-431);
  base Read/Write raise "<ClassName>.Read not implemented"; open and create failures raise EFOpenError/EFCreateError
  with the expanded path and the system message; THandleStream does not close its handle. }
uses
  Windows, SysUtils, Classes, TestKit;

type
  TBare = class(TStream)
  public
    function Seek(const Offset: Int64; Origin: TSeekOrigin): Int64; override;
  end;

function TBare.Seek(const Offset: Int64; Origin: TSeekOrigin): Int64;
begin
  Result := 0;
end;

var
  Dir, Name, Msg: string;
  F, G: TFileStream;
  H: THandleStream;
  Buf: array[0..15] of Byte;
  I: Integer;
  B: TBare;
  Handle: THandle;
begin
  Dir := IncludeTrailingPathDelimiter(ParamStr(1));
  Name := Dir + 'data.bin';

  Section('write and read');
  F := TFileStream.Create(Name, fmCreate);
  for I := 0 to 9 do
    Buf[I] := I * 3;
  F.WriteBuffer(Buf, 10);
  CheckEqI(10, F.Position, 'position after write');
  CheckEqI(10, F.Size, 'size');
  CheckEqI(10, F.Position, 'Size keeps the position');
  CheckEqS(Name, F.FileName, 'FileName');
  F.Free;
  F := TFileStream.Create(Name, fmOpenRead);
  FillChar(Buf, SizeOf(Buf), 0);
  F.Position := 4;
  F.ReadBuffer(Buf, 3);
  CheckEqI(12, Buf[0], 'read at position 4');
  CheckEqI(18, Buf[2], 'third byte');
  CheckEqI(7, F.Position, 'position after read');
  CheckEqI(3, F.Read(Buf, 16), 'Read returns fewer at the end');
  CheckEqI(0, F.Read(Buf, 16), 'Read at the end');
  CheckEqI(2, F.Seek(-8, soEnd), 'seek from the end');
  CheckEqI(5, F.Seek(3, soCurrent), 'seek from current');
  Msg := '';
  try
    F.ReadBuffer(Buf, 8);
  except
    on E: EReadError do
      Msg := E.Message;
  end;
  CheckEqS('Stream read error', Msg, 'ReadBuffer past the end');
  F.Free;

  Section('size and copy');
  F := TFileStream.Create(Name, fmOpenReadWrite);
  F.Size := 6;
  CheckEqI(6, F.Size, 'SetSize truncates');
  G := TFileStream.Create(Dir + 'copy.bin', fmCreate);
  F.Position := 1;
  CheckEqI(3, G.CopyFrom(F, 3), 'CopyFrom count');
  CheckEqI(4, F.Position, 'source advanced');
  CheckEqI(6, G.CopyFrom(F, 0), 'CopyFrom 0 copies everything');
  CheckEqI(9, G.Size, 'copied bytes');
  G.Position := 3;
  G.ReadBuffer(Buf, 2);
  CheckEqI(0, Buf[0], 'first byte of the full copy');
  CheckEqI(3, Buf[1], 'second byte of the full copy');
  G.Free;
  F.Free;

  Section('errors');
  B := TBare.Create;
  Msg := '';
  try
    B.Read(Buf, 1);
  except
    on E: EStreamError do
      Msg := E.Message;
  end;
  CheckEqS('TBare.Read not implemented', Msg, 'base Read');
  Msg := '';
  try
    B.Write(Buf, 1);
  except
    on E: EStreamError do
      Msg := E.Message;
  end;
  CheckEqS('TBare.Write not implemented', Msg, 'base Write');
  B.Free;
  Msg := '';
  try
    TFileStream.Create(Dir + 'missing.bin', fmOpenRead);
  except
    on E: EFOpenError do
      Msg := E.Message;
  end;
  CheckEqS('Cannot open file "' + Dir + 'missing.bin". ' + SysErrorMessage(ERROR_FILE_NOT_FOUND), Msg, 'open failure');
  Msg := '';
  try
    TFileStream.Create(Dir + 'no\such\dir\x.bin', fmCreate);
  except
    on E: EFCreateError do
      Msg := E.Message;
  end;
  CheckEqS('Cannot create file "' + Dir + 'no\such\dir\x.bin". ' + SysErrorMessage(ERROR_PATH_NOT_FOUND), Msg,
    'create failure');

  Section('THandleStream');
  Handle := FileOpen(Name, fmOpenRead);
  H := THandleStream.Create(Handle);
  CheckEqI(6, H.Size, 'size through the handle');
  H.Free;
  CheckEqI(6, FileSeek(Handle, 0, 2), 'handle still open after Free');
  FileClose(Handle);
  Done;
end.
