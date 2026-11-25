// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1356
// needs: desktop
// phase: 8
program Ctl_ImagePng;
{ U-149, wic-image.md section 3: a form whose image control holds a PNG. Picture.Data of Ctl_ImagePng.form is the
  class name TWICImage followed by the bytes of the fixture alpha.png: the form loads, the picture is a TWICImage
  of the fixture's size, and the control painted into a memory bitmap (WM_PRINTCLIENT) over the form colour shows
  the opaque blocks, the half-transparent block blended over the form colour and the form colour where the image
  is transparent; the checksum of all pixel rows of the capture is recorded. The picture written back has the
  bytes of the form. Ctl_ImagePngBroken.form holds the first 40 bytes of the same image: loading it raises
  EInvalidGraphic. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, ExtCtrls, TestKit, WicTestKit;

type
  TPngForm = class(TForm)
  published
    Photo: TImage;
  end;

  TBrokenForm = class(TForm)
  published
    Photo: TImage;
  end;

  TCaptureFiler = class(TFiler)
  public
    Bytes: TMemoryStream;
    procedure DefineProperty(const Name: string; ReadData: TReaderProc; WriteData: TWriterProc;
      HasData: Boolean); override;
    procedure DefineBinaryProperty(const Name: string; ReadData, WriteData: TStreamProc;
      HasData: Boolean); override;
  end;

  TPictureAccess = class(TPicture)
  public
    procedure Define(Filer: TFiler);
  end;

{$FORM Ctl_ImagePng.form}
{$FORM Ctl_ImagePngBroken.form}

const
  FormColour = TColor($00C08040);                          { red $40, green $80, blue $C0 }

var
  Bmp: TBitmap;

procedure TCaptureFiler.DefineProperty(const Name: string; ReadData: TReaderProc; WriteData: TWriterProc;
  HasData: Boolean);
begin
end;

procedure TCaptureFiler.DefineBinaryProperty(const Name: string; ReadData, WriteData: TStreamProc;
  HasData: Boolean);
begin
  if HasData and Assigned(WriteData) then
    WriteData(Bytes);
end;

procedure TPictureAccess.Define(Filer: TFiler);
begin
  DefineProperties(Filer);
end;

procedure Capture(F: TForm);
begin
  Bmp.Canvas.Brush.Color := clBlack;
  Bmp.Canvas.FillRect(Rect(0, 0, Bmp.Width, Bmp.Height));
  SendMessageW(F.Handle, WM_PRINTCLIENT, WPARAM(Bmp.Canvas.Handle), PRF_CLIENT or PRF_ERASEBKGND);
end;

{ source-over of a premultiplied channel S with alpha A over D }
function Over(S, A, D: Integer): Integer;
begin
  Result := S + (D * (255 - A) + 127) div 255;
end;

{ a 32-bit sum over the red, green and blue values of every pixel, row by row }
function Checksum: Cardinal;
var
  X, Y: Integer;
  C: Cardinal;
begin
  Result := 2166136261;
  for Y := 0 to Bmp.Height - 1 do
    for X := 0 to Bmp.Width - 1 do
    begin
      C := Cardinal(Bmp.Canvas.Pixels[X, Y]) and $FFFFFF;
      Result := Cardinal((Int64(Result xor C) * 16777619) and $FFFFFFFF);
    end;
end;

procedure Run;
var
  F: TPngForm;
  Broken: TBrokenForm;
  Image: TWICImage;
  Original, Written, Tail: TMemoryStream;
  Filer: TCaptureFiler;
  Half: TColor;
  P: PByte;
  First, Second: Cardinal;
begin
  Application.Initialize;
  Bmp := TBitmap.Create;
  Bmp.PixelFormat := pf32bit;
  Bmp.SetSize(60, 40);
  Original := FileBytes(Fixture('alpha.png'));
  Written := TMemoryStream.Create;
  Tail := TMemoryStream.Create;
  Filer := TCaptureFiler.Create(nil, 0);
  Filer.Bytes := Written;
  F := TPngForm.Create(nil);
  try
    Section('the form loads (UIF-1356)');
    Check(F.Photo.Picture.Graphic is TWICImage, 'the picture is a TWICImage');
    Image := TWICImage(F.Photo.Picture.Graphic);
    CheckEqI(16, Image.Width, 'Width');
    CheckEqI(12, Image.Height, 'Height');
    Check(Image.ImageFormat = wifPng, 'ImageFormat');
    CheckEqB(True, Image.HasAlpha, 'HasAlpha');
    CheckEqI(16, F.Photo.Width, 'the control keeps its stored width');
    CheckEqI(12, F.Photo.Height, 'the control keeps its stored height');

    Section('the control paints the image with its alpha over the form colour');
    Half := TColor(Over(0, 128, $40) or (Over(0, 128, $80) shl 8) or (Over(128, 128, $C0) shl 16));
    Capture(F);
    CheckQuad(Cardinal(clRed), Cardinal(Bmp.Canvas.Pixels[14, 13]), 0, 'the opaque red block');
    CheckQuad(Cardinal(clLime), Cardinal(Bmp.Canvas.Pixels[22, 13]), 0, 'the opaque green block');
    CheckQuad(Cardinal(Half), Cardinal(Bmp.Canvas.Pixels[14, 19]), 1, 'the half-transparent block is blended');
    CheckQuad(Cardinal(FormColour), Cardinal(Bmp.Canvas.Pixels[22, 19]), 0, 'the transparent block shows the form');
    CheckQuad(Cardinal(FormColour), Cardinal(Bmp.Canvas.Pixels[5, 5]), 0, 'the form beside the control');
    First := Checksum;
    Capture(F);
    Second := Checksum;
    CheckEqI(First, Second, 'a second capture has the same checksum');
    Writeln('checksum ', IntToHex(Int64(First), 8));

    Section('the picture written back');
    TPictureAccess(F.Photo.Picture).Define(Filer);
    CheckEqI(Original.Size + 10, Written.Size, 'the length byte, the class name and the bytes of the image');
    P := PByte(Written.Memory);
    CheckEqI(9, P^, 'the length of the class name');
    Written.Position := 10;
    Tail.CopyFrom(Written, Written.Size - 10);
    Check(SameBytes(Original, Tail), 'the bytes after the class name are those of the image file');
  finally
    F.Free;
  end;

  Section('a form with a damaged image');
  Broken := nil;
  Expect('EInvalidGraphic', 'The image is damaged or its format is not supported');
  try
    Broken := TBrokenForm.Create(nil);
    Check(False, 'loading raises');
  except
    on E: Exception do
      CheckRaises('loading raises', E.ClassName, E.Message);
  end;
  Broken.Free;
  Filer.Free;
  Tail.Free;
  Written.Free;
  Original.Free;
  Bmp.Free;
end;

begin
  Run;
  Done;
end.
