// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1352, UIF-1353, UIF-1354, UIF-1355
// phase: 8
// opc-run: tempdir
program Gfx_PictureFormats;
{ U-149, wic-image.md section 3: TPicture.LoadFromFile chooses the graphic class by the content of the file
  first and by its extension second. Each fixture image gives its class; copies under a foreign extension follow
  the content; a file of unknown content goes to the class of its extension, which reports its own error; a file
  that is unknown in both ways raises the unknown-extension error; a failed load keeps the picture; the class of
  a recognised content is the one registered for that format's extension, so a later registration still wins;
  Picture.Bitmap on an image picture converts the pixels instead of discarding them. Needs no desktop. }
uses
  Windows, SysUtils, Classes, Graphics, TestKit, WicTestKit;

type
  TMyImage = class(TWICImage);

const
  Damaged = 'The image is damaged or its format is not supported';
  { a 1 x 1 GIF with the older of the two version marks, built by hand }
  OldGif: array[0..40] of Byte = (
    $47, $49, $46, $38, $37, $61, $01, $00, $01, $00, $F1, $00, $00,
    $00, $00, $00, $0A, $C8, $1E, $1E, $0A, $C8, $FF, $FF, $FF,
    $2C, $00, $00, $00, $00, $01, $00, $01, $00, $00, $02, $02, $4C, $01, $00,
    $3B);

var
  TempDir: string;

function Temp(const Name: string): string;
begin
  Result := TempDir + '\' + Name;
end;

procedure CopyFixture(const Name, NewName: string);
var
  M: TMemoryStream;
begin
  M := FileBytes(Fixture(Name));
  try
    M.SaveToFile(Temp(NewName));
  finally
    M.Free;
  end;
end;

procedure WriteBytes(const NewName: string; const Bytes; Count: Integer);
var
  F: TFileStream;
begin
  F := TFileStream.Create(Temp(NewName), fmCreate);
  try
    if Count > 0 then
      F.WriteBuffer(Bytes, Count);
  finally
    F.Free;
  end;
end;

{ a 16 x 16 32-bit icon file, every pixel opaque blue (as in Gfx_Picture) }
procedure WriteIcoFile(const NewName: string);
var
  F: TFileStream;
  I: Integer;

  procedure W8(V: Byte);
  begin
    F.WriteBuffer(V, 1);
  end;

  procedure W16(V: Word);
  begin
    F.WriteBuffer(V, 2);
  end;

  procedure W32(V: Cardinal);
  begin
    F.WriteBuffer(V, 4);
  end;

begin
  F := TFileStream.Create(Temp(NewName), fmCreate);
  try
    W16(0); W16(1); W16(1);
    W8(16); W8(16); W8(0); W8(0); W16(1); W16(32); W32(40 + 1024 + 64); W32(22);
    W32(40); W32(16); W32(32); W16(1); W16(32); W32(0); W32(0); W32(0); W32(0); W32(0); W32(0);
    for I := 1 to 256 do
      W32($FF0000FF);
    for I := 1 to 16 do
      W32(0);
  finally
    F.Free;
  end;
end;

{ the class name of the graphic the picture holds after loading FileName }
function LoadedClass(P: TPicture; const FileName: string): string;
begin
  P.LoadFromFile(FileName);
  Result := P.Graphic.ClassName;
end;

procedure CheckRaisesLoad(P: TPicture; const FileName, Name: string);
begin
  try
    P.LoadFromFile(FileName);
    Check(False, Name);
  except
    on E: Exception do
      CheckRaises(Name, E.ClassName, E.Message);
  end;
end;

procedure CheckFixtures;
var
  P: TPicture;
begin
  Section('each fixture gives its class (UIF-1352, UIF-1353)');
  P := TPicture.Create;
  try
    CheckEqS('TWICImage', LoadedClass(P, Fixture('alpha.png')), 'alpha.png');
    CheckEqI(16, P.Width, 'alpha.png: Width');
    CheckEqI(12, P.Height, 'alpha.png: Height');
    Check(TWICImage(P.Graphic).ImageFormat = wifPng, 'alpha.png: ImageFormat');
    CheckEqS('TBitmap', LoadedClass(P, Fixture('plain.bmp')), 'plain.bmp');
    CheckEqI(clBlue, P.Bitmap.Canvas.Pixels[4, 9], 'plain.bmp: loaded by the framework''s own reader');
    CheckEqS('TWICImage', LoadedClass(P, Fixture('blocks.gif')), 'blocks.gif');
    Check(TWICImage(P.Graphic).ImageFormat = wifGif, 'blocks.gif: ImageFormat');
    CheckEqS('TWICImage', LoadedClass(P, Fixture('baseline.jpg')), 'baseline.jpg');
    Check(TWICImage(P.Graphic).ImageFormat = wifJpeg, 'baseline.jpg: ImageFormat');
    CheckEqS('TWICImage', LoadedClass(P, Fixture('progressive.jpg')), 'progressive.jpg');
    CheckEqS('TWICImage', LoadedClass(P, Fixture('rotated.jpg')), 'rotated.jpg');
    CheckEqI(32, P.Width, 'rotated.jpg: Width as stored');
    WriteIcoFile('blue.ico');
    CheckEqS('TIcon', LoadedClass(P, Temp('blue.ico')), 'an icon file');
  finally
    P.Free;
  end;
end;

procedure CheckContentFirst;
var
  P: TPicture;
begin
  Section('the content decides whatever the extension is (UIF-1353, UIF-1354)');
  P := TPicture.Create;
  try
    CopyFixture('alpha.png', 'alpha.bmp');
    CopyFixture('alpha.png', 'alpha.dat');
    CopyFixture('alpha.png', 'alpha');
    CopyFixture('plain.bmp', 'plain.png');
    CopyFixture('plain.bmp', 'plain.xyz');
    CopyFixture('blocks.gif', 'blocks.jpg');
    CopyFixture('baseline.jpg', 'baseline.ico');
    CopyFixture('baseline.jpg', 'BASELINE.JPEG');
    WriteIcoFile('blue.png');
    WriteBytes('old.dat', OldGif, SizeOf(OldGif));
    CheckEqS('TWICImage', LoadedClass(P, Temp('alpha.bmp')), 'a PNG named .bmp');
    CheckEqI(16, P.Width, 'a PNG named .bmp: Width');
    CheckEqS('TWICImage', LoadedClass(P, Temp('alpha.dat')), 'a PNG named .dat');
    CheckEqS('TWICImage', LoadedClass(P, Temp('alpha')), 'a PNG without an extension');
    CheckEqS('TBitmap', LoadedClass(P, Temp('plain.png')), 'a bitmap named .png');
    CheckEqI(clYellow, P.Bitmap.Canvas.Pixels[12, 9], 'a bitmap named .png: pixels');
    CheckEqS('TBitmap', LoadedClass(P, Temp('plain.xyz')), 'a bitmap named .xyz');
    CheckEqS('TWICImage', LoadedClass(P, Temp('blocks.jpg')), 'a GIF named .jpg');
    Check(TWICImage(P.Graphic).ImageFormat = wifGif, 'a GIF named .jpg: ImageFormat');
    CheckEqS('TWICImage', LoadedClass(P, Temp('baseline.ico')), 'a JPEG named .ico');
    CheckEqS('TWICImage', LoadedClass(P, Temp('BASELINE.JPEG')), 'a JPEG named .JPEG');
    CheckEqS('TIcon', LoadedClass(P, Temp('blue.png')), 'an icon named .png');
    CheckEqS('TWICImage', LoadedClass(P, Temp('old.dat')), 'a GIF of the older version mark named .dat');
    CheckEqI(1, P.Width, 'a GIF of the older version mark: Width');
  finally
    P.Free;
  end;
end;

procedure CheckExtensionSecond;
var
  P: TPicture;
  Counter: TChangeCounter;
  One: Byte;
begin
  Section('unknown content: the extension decides (UIF-1352, UIF-1354)');
  P := TPicture.Create;
  Counter := TChangeCounter.Create;
  try
    P.LoadFromFile(Fixture('plain.bmp'));
    P.OnChange := Counter.Changed;
    CopyFixture('not-an-image.png', 'text.jpg');
    CopyFixture('not-an-image.png', 'text.jpeg');
    CopyFixture('not-an-image.png', 'text.gif');
    CopyFixture('not-an-image.png', 'text.bmp');
    CopyFixture('not-an-image.png', 'text.dat');
    CopyFixture('not-an-image.png', 'text');
    One := Ord('B');
    WriteBytes('one.dat', One, 1);
    WriteBytes('none.png', One, 0);
    Expect('EInvalidGraphic', Damaged);
    CheckRaisesLoad(P, Fixture('not-an-image.png'), 'text named .png goes to the image class');
    CheckRaisesLoad(P, Temp('text.jpg'), 'text named .jpg goes to the image class');
    CheckRaisesLoad(P, Temp('text.jpeg'), 'text named .jpeg goes to the image class');
    CheckRaisesLoad(P, Temp('text.gif'), 'text named .gif goes to the image class');
    CheckRaisesLoad(P, Fixture('broken.png'), 'a cut PNG goes to the image class');
    CheckRaisesLoad(P, Temp('none.png'), 'a file without bytes named .png goes to the image class');
    Expect('EInvalidGraphic', 'Bitmap image is not valid');
    CheckRaisesLoad(P, Temp('text.bmp'), 'text named .bmp goes to the bitmap class');
    Expect('EInvalidGraphic', 'Unknown picture file extension (.dat)');
    CheckRaisesLoad(P, Temp('text.dat'), 'text named .dat: unknown in both ways');
    CheckRaisesLoad(P, Temp('one.dat'), 'one byte named .dat: unknown in both ways');
    CheckRaisesLoad(P, Temp('missing.dat'), 'a missing file with an unknown extension');
    Expect('EInvalidGraphic', 'Unknown picture file extension (.)');
    CheckRaisesLoad(P, Temp('text'), 'text without an extension');
    try
      P.LoadFromFile(Temp('missing.png'));
      Check(False, 'a missing file with a known extension raises the error of the file stream');
    except
      on E: Exception do
        CheckEqS('EFOpenError', E.ClassName, 'a missing file with a known extension raises the error of the file stream');
    end;
    CheckEqI(0, Counter.Count, 'no failed load fires OnChange');
    Check(P.Graphic is TBitmap, 'the picture keeps its graphic');
    CheckEqI(16, P.Width, 'and its size');
  finally
    Counter.Free;
    P.Free;
  end;
end;

procedure CheckRegistry;
var
  P: TPicture;
begin
  Section('a later registration still wins (graphics.md section 4.3)');
  P := TPicture.Create;
  try
    TPicture.RegisterFileFormat('png', 'My images', TMyImage);
    CheckEqS('TMyImage', LoadedClass(P, Fixture('alpha.png')), 'a PNG after a later registration of png');
    CheckEqS('TMyImage', LoadedClass(P, Temp('alpha.dat')), 'also when the content decides');
    CheckEqS('TWICImage', LoadedClass(P, Fixture('blocks.gif')), 'another format is not concerned');
    TPicture.RegisterFileFormat('png', 'PNG images', TWICImage);
    CheckEqS('TWICImage', LoadedClass(P, Fixture('alpha.png')), 'registered back');
  finally
    P.Free;
  end;
end;

procedure CheckBitmap;
var
  P: TPicture;
  B: TBitmap;
  Counter: TChangeCounter;
begin
  Section('Picture.Bitmap converts an image (UIF-1355)');
  P := TPicture.Create;
  Counter := TChangeCounter.Create;
  try
    P.LoadFromFile(Fixture('alpha.png'));
    P.OnChange := Counter.Changed;
    B := P.Bitmap;
    Check(P.Graphic = B, 'the picture holds the bitmap');
    CheckEqS('TBitmap', P.Graphic.ClassName, 'it is a TBitmap');
    CheckEqI(1, Counter.Count, 'OnChange fires once');
    Check(B.PixelFormat = pf32bit, 'the bitmap is 32-bit');
    CheckEqI(16, B.Width, 'Width');
    CheckEqI(12, B.Height, 'Height');
    CheckEqS('FFFF0000', QuadText(Quad(B, 4, 3)), 'the red block');
    CheckEqS('FF00FF00', QuadText(Quad(B, 12, 3)), 'the green block');
    CheckEqS('80000080', QuadText(Quad(B, 4, 9)), 'the half-transparent block with its alpha');
    CheckEqS('00000000', QuadText(Quad(B, 12, 9)), 'the transparent block');
    Check(P.Bitmap = B, 'asking again returns the same bitmap');
    CheckEqI(1, Counter.Count, 'without another change');

    P.LoadFromFile(Temp('blue.ico'));
    CheckEqB(True, P.Bitmap.Empty, 'an icon picture still gives an empty bitmap');
    P.Graphic := nil;
    CheckEqB(True, P.Bitmap.Empty, 'an empty picture still gives an empty bitmap');
    P.LoadFromFile(Fixture('alpha.png'));
    P.Graphic.Assign(nil);
    CheckEqB(True, P.Bitmap.Empty, 'an empty image gives an empty bitmap');
  finally
    Counter.Free;
    P.Free;
  end;
end;

begin
  TempDir := ParamStr(1);
  CheckFixtures;
  CheckContentFirst;
  CheckExtensionSecond;
  CheckRegistry;
  CheckBitmap;
  Done;
end.
