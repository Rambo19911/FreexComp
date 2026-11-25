// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1347, UIF-1348, UIF-1349, UIF-1350
// phase: 8
// opc-run: tempdir
program Gfx_WicImageAssignSave;
{ U-148, wic-image.md section 2: Assign from a 24-bit and a 32-bit bitmap (the pixels become opaque whatever the
  fourth byte holds), from an empty bitmap, from nil, from another image (deep copy) and from an object the class
  cannot convert; a bitmap assigned from an image receives the premultiplied pixels with their alpha bytes; an
  image that was loaded saves exactly the bytes it was loaded from, at the stream's position, and a second image
  loaded from them is equal (pixels, properties, bytes); an image assigned from a bitmap and an empty image raise
  and write nothing; a picture that holds an image without stored bytes cannot be written. Needs no desktop. }
uses
  Windows, Types, SysUtils, Classes, Graphics, TestKit, WicTestKit;

type
  { a filer that writes a defined binary property into Bytes (as Gfx_Picture does) }
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

var
  TempDir: string;

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

procedure SetQuad(B: TBitmap; X, Y: Integer; V: Cardinal);
var
  P: PByte;
begin
  P := PByte(B.ScanLine[Y]);
  Inc(P, X * 4);
  PCardinal(P)^ := V;
end;

{ True when both images have the same size, properties and pixels }
function SameImage(A, B: TWICImage): Boolean;
var
  BA, BB: TBitmap;
  X, Y: Integer;
begin
  Result := (A.Width = B.Width) and (A.Height = B.Height) and (A.Empty = B.Empty) and
    (A.ImageFormat = B.ImageFormat) and (A.HasAlpha = B.HasAlpha);
  if not Result or A.Empty then
    Exit;
  BA := TBitmap.Create;
  BB := TBitmap.Create;
  try
    BA.Assign(A);
    BB.Assign(B);
    for Y := 0 to BA.Height - 1 do
      for X := 0 to BA.Width - 1 do
        if Quad(BA, X, Y) <> Quad(BB, X, Y) then
          Result := False;
  finally
    BA.Free;
    BB.Free;
  end;
end;

procedure CheckFromBitmap;
var
  Image: TWICImage;
  B: TBitmap;
  Counter: TChangeCounter;
  List: TStringList;
begin
  Section('Assign from a bitmap (UIF-1347)');
  Image := TWICImage.Create;
  B := TBitmap.Create;
  Counter := TChangeCounter.Create;
  List := TStringList.Create;
  try
    Image.OnChange := Counter.Changed;
    B.PixelFormat := pf24bit;
    B.SetSize(3, 2);
    B.Canvas.Pixels[0, 0] := TColor($00332211);            { red $11, green $22, blue $33 }
    B.Canvas.Pixels[2, 1] := clRed;
    Image.Assign(B);
    CheckEqI(1, Counter.Count, '24-bit: OnChange fires once');
    CheckEqB(False, Image.Empty, '24-bit: not empty');
    CheckEqI(3, Image.Width, '24-bit: Width');
    CheckEqI(2, Image.Height, '24-bit: Height');
    CheckEqB(False, Image.HasAlpha, '24-bit: HasAlpha');
    Check(Image.ImageFormat = wifUnknown, '24-bit: ImageFormat is wifUnknown');
    CheckEqS('FF112233', QuadText(ImageQuad(Image, 0, 0)), '24-bit: a pixel');
    CheckEqS('FFFF0000', QuadText(ImageQuad(Image, 2, 1)), '24-bit: the last pixel');
    CheckEqS('FFFFFFFF', QuadText(ImageQuad(Image, 1, 0)), '24-bit: an untouched pixel is opaque white');

    B.PixelFormat := pf32bit;
    B.Canvas.Pixels[1, 1] := clBlue;                       { GDI leaves the fourth byte 0 }
    SetQuad(B, 1, 0, $40102030);                           { a fourth byte of 64 }
    Counter.Count := 0;
    Image.Assign(B);
    CheckEqI(1, Counter.Count, '32-bit: OnChange fires once');
    CheckEqB(False, Image.HasAlpha, '32-bit: HasAlpha');
    CheckEqS('FF0000FF', QuadText(ImageQuad(Image, 1, 1)), '32-bit: a pixel drawn with GDI is opaque');
    CheckEqS('FF102030', QuadText(ImageQuad(Image, 1, 0)), '32-bit: the fourth byte of the bitmap is not taken');
    CheckEqS('00112233', QuadText(Quad(B, 0, 0)), '32-bit: the source bitmap is unchanged');

    Image.LoadFromFile(Fixture('alpha.png'));
    Counter.Count := 0;
    Image.Assign(B);
    Check(Image.ImageFormat = wifUnknown, 'over a loaded image: ImageFormat is wifUnknown');
    CheckEqB(False, Image.HasAlpha, 'over a loaded image: HasAlpha');
    CheckEqI(3, Image.Width, 'over a loaded image: Width');

    B.SetSize(0, 0);
    Counter.Count := 0;
    Image.Assign(B);
    CheckEqI(1, Counter.Count, 'an empty bitmap: OnChange fires once');
    CheckEqB(True, Image.Empty, 'an empty bitmap gives an empty image');

    Image.LoadFromFile(Fixture('alpha.png'));
    Counter.Count := 0;
    Image.Assign(nil);
    CheckEqI(1, Counter.Count, 'nil: OnChange fires once');
    CheckEqB(True, Image.Empty, 'nil: empty');
    CheckEqI(0, Image.Width, 'nil: Width');
    Check(Image.ImageFormat = wifUnknown, 'nil: ImageFormat');
    CheckEqB(False, Image.HasAlpha, 'nil: HasAlpha');

    Image.LoadFromFile(Fixture('alpha.png'));
    Counter.Count := 0;
    try
      Image.Assign(List);
      Check(False, 'an object the class cannot convert raises');
    except
      on E: Exception do
        CheckEqS('EConvertError', E.ClassName, 'an object the class cannot convert raises');
    end;
    CheckEqI(0, Counter.Count, 'and changes nothing');
    CheckEqI(16, Image.Width, 'the image is still there');
  finally
    List.Free;
    Counter.Free;
    B.Free;
    Image.Free;
  end;
end;

procedure CheckToBitmap;
var
  Image: TWICImage;
  B: TBitmap;
  List: TStringList;
begin
  Section('a bitmap assigned from an image (UIF-1348)');
  Image := TWICImage.Create;
  B := TBitmap.Create;
  List := TStringList.Create;
  try
    Image.LoadFromFile(Fixture('alpha.png'));
    B.PixelFormat := pf24bit;
    B.SetSize(5, 5);
    B.Assign(Image);
    Check(B.PixelFormat = pf32bit, 'the bitmap is 32-bit');
    CheckEqI(16, B.Width, 'Width');
    CheckEqI(12, B.Height, 'Height');
    CheckEqS('FFFF0000', QuadText(Quad(B, 4, 3)), 'an opaque pixel');
    CheckEqS('80000080', QuadText(Quad(B, 4, 9)), 'the half-transparent pixel: premultiplied, alpha 128');
    CheckEqS('00000000', QuadText(Quad(B, 12, 9)), 'the transparent pixel: alpha 0');
    B.Canvas.Pixels[0, 0] := clBlue;
    CheckEqS('FFFF0000', QuadText(ImageQuad(Image, 0, 0)), 'the bitmap is a copy: the image is unchanged');
    Image.Assign(nil);
    B.Assign(Image);
    CheckEqB(True, B.Empty, 'an empty image gives an empty bitmap');
    try
      List.Assign(Image);
      Check(False, 'another destination raises');
    except
      on E: Exception do
        CheckEqS('EConvertError', E.ClassName, 'another destination raises');
    end;
  finally
    List.Free;
    B.Free;
    Image.Free;
  end;
end;

procedure CheckImageToImage;
var
  A, B: TWICImage;
  Counter: TChangeCounter;
  SA, SB: TMemoryStream;
begin
  Section('Assign from an image (UIF-1347)');
  A := TWICImage.Create;
  B := TWICImage.Create;
  Counter := TChangeCounter.Create;
  SA := FileBytes(Fixture('alpha.png'));
  SB := TMemoryStream.Create;
  try
    A.LoadFromFile(Fixture('alpha.png'));
    B.OnChange := Counter.Changed;
    B.Assign(A);
    CheckEqI(1, Counter.Count, 'OnChange fires once');
    Check(SameImage(A, B), 'size, properties and pixels are equal');
    Check(B.ImageFormat = wifPng, 'ImageFormat');
    CheckEqB(True, B.HasAlpha, 'HasAlpha');
    A.Assign(nil);
    CheckEqI(16, B.Width, 'the copy is independent of the source');
    CheckEqS('80000080', QuadText(ImageQuad(B, 4, 9)), 'the copy keeps its pixels');
    B.SaveToStream(SB);
    Check(SameBytes(SA, SB), 'the copy has the stored bytes');
    Counter.Count := 0;
    B.Assign(B);
    CheckEqI(0, Counter.Count, 'assigning an image to itself does nothing');
    CheckEqI(16, B.Width, 'and keeps it');
    B.Assign(A);
    CheckEqB(True, B.Empty, 'an empty image gives an empty image');
  finally
    SB.Free;
    SA.Free;
    Counter.Free;
    B.Free;
    A.Free;
  end;
end;

procedure CheckRoundTrip(const Name: string);
var
  A, B: TWICImage;
  Original, Saved, Again, FromFile: TMemoryStream;
  Junk: array[0..2] of Byte;
  P: PByte;
begin
  Section('save and reload: ' + Name + ' (UIF-1349, UIF-1350)');
  A := TWICImage.Create;
  B := TWICImage.Create;
  Original := FileBytes(Fixture(Name));
  Saved := TMemoryStream.Create;
  Again := TMemoryStream.Create;
  FromFile := nil;
  try
    A.LoadFromFile(Fixture(Name));
    A.SaveToStream(Saved);
    Check(SameBytes(Original, Saved), 'the saved bytes are the bytes of the file');
    Saved.Position := 0;
    B.LoadFromStream(Saved);
    Check(SameImage(A, B), 'the image loaded from them is equal');
    B.SaveToStream(Again);
    Check(SameBytes(Original, Again), 'and saves the same bytes again');

    Again.Clear;
    Junk[0] := 1;
    Junk[1] := 2;
    Junk[2] := 3;
    Again.WriteBuffer(Junk, 3);
    A.SaveToStream(Again);
    CheckEqI(Original.Size + 3, Again.Size, 'the bytes are written at the position of the stream');
    P := PByte(Again.Memory);
    Inc(P, 2);
    CheckEqI(3, P^, 'what was before is kept');

    A.SaveToFile(TempDir + '\saved.dat');
    FromFile := FileBytes(TempDir + '\saved.dat');
    Check(SameBytes(Original, FromFile), 'SaveToFile writes the same bytes, whatever the extension');
  finally
    FromFile.Free;
    Again.Free;
    Saved.Free;
    Original.Free;
    B.Free;
    A.Free;
  end;
end;

procedure CheckNoData;
var
  Image: TWICImage;
  B: TBitmap;
  M: TMemoryStream;
  P: TPicture;
  F: TCaptureFiler;
begin
  Section('images that cannot be saved (UIF-1349, UIF-1350)');
  Image := TWICImage.Create;
  B := TBitmap.Create;
  M := TMemoryStream.Create;
  P := TPicture.Create;
  F := TCaptureFiler.Create(nil, 0);
  F.Bytes := TMemoryStream.Create;
  try
    Expect('EInvalidGraphicOperation', 'The image is empty');
    try
      Image.SaveToStream(M);
      Check(False, 'an empty image raises');
    except
      on E: Exception do
        CheckRaises('an empty image raises', E.ClassName, E.Message);
    end;
    CheckEqI(0, M.Size, 'nothing is written');

    B.SetSize(2, 2);
    Image.Assign(B);
    Expect('EInvalidGraphicOperation', 'The image has no encoded data to save');
    try
      Image.SaveToStream(M);
      Check(False, 'an image assigned from a bitmap raises');
    except
      on E: Exception do
        CheckRaises('an image assigned from a bitmap raises', E.ClassName, E.Message);
    end;
    CheckEqI(0, M.Size, 'nothing is written');
    try
      Image.SaveToFile(TempDir + '\nodata.png');
      Check(False, 'SaveToFile raises too');
    except
      on E: Exception do
        CheckRaises('SaveToFile raises too', E.ClassName, E.Message);
    end;

    B.Assign(Image);
    B.SaveToStream(M);
    Check(M.Size > 0, 'such an image is saved through a bitmap');

    P.Graphic := Image;
    Check(P.Graphic is TWICImage, 'a picture holds a copy of the image');
    try
      TPictureAccess(P).Define(F);
      Check(False, 'writing the picture raises');
    except
      on E: Exception do
        CheckRaises('writing the picture raises', E.ClassName, E.Message);
    end;
  finally
    F.Bytes.Free;
    F.Free;
    P.Free;
    M.Free;
    B.Free;
    Image.Free;
  end;
end;

begin
  TempDir := ParamStr(1);
  CheckFromBitmap;
  CheckToBitmap;
  CheckImageToImage;
  CheckRoundTrip('alpha.png');
  CheckRoundTrip('plain.bmp');
  CheckRoundTrip('blocks.gif');
  CheckRoundTrip('baseline.jpg');
  CheckNoData;
  Done;
end.
