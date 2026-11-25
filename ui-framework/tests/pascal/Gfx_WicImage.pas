// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1340, UIF-1342, UIF-1344, UIF-1345, UIF-1351
// phase: 8
program Gfx_WicImage;
{ U-148, wic-image.md section 2: TWICImage. A new image is empty; each fixture image (PNG with alpha, bitmap, GIF,
  baseline and progressive JPEG) loads from its file and from a stream with the expected size, container format,
  HasAlpha and four block colours (premultiplied; exact for the lossless formats, within 4 per channel for JPEG);
  a load starts at the stream's position and leaves it at the end; OnChange fires once and Modified is False; a
  file of two frames gives its first frame; the size cannot be set. The class is registered for streaming and the
  library of the imaging component is not in the process before the first load. Needs no desktop. }
uses
  Windows, SysUtils, Classes, Graphics, TestKit, WicTestKit;

const
  { a 1 x 1 GIF of two frames, built by hand: frame 0 is colour 1 (10, 200, 30), frame 1 is colour 2 (30, 10, 200) }
  TwoFrames: array[0..55] of Byte = (
    $47, $49, $46, $38, $39, $61, $01, $00, $01, $00, $F1, $00, $00,
    $00, $00, $00, $0A, $C8, $1E, $1E, $0A, $C8, $FF, $FF, $FF,
    $2C, $00, $00, $00, $00, $01, $00, $01, $00, $00, $02, $02, $4C, $01, $00,
    $2C, $00, $00, $00, $00, $01, $00, $01, $00, $00, $02, $02, $54, $01, $00,
    $3B);

function FormatName(F: TWICImageFormat): string;
begin
  case F of
    wifUnknown: Result := 'wifUnknown';
    wifBmp: Result := 'wifBmp';
    wifPng: Result := 'wifPng';
    wifJpeg: Result := 'wifJpeg';
    wifGif: Result := 'wifGif';
    wifTiff: Result := 'wifTiff';
  else
    Result := 'wifOther';
  end;
end;

procedure CheckDefaults;
var
  Image: TWICImage;
begin
  Section('a new image (UIF-1340)');
  Image := TWICImage.Create;
  try
    CheckEqB(True, Image.Empty, 'Empty');
    CheckEqI(0, Image.Width, 'Width');
    CheckEqI(0, Image.Height, 'Height');
    CheckEqS('wifUnknown', FormatName(Image.ImageFormat), 'ImageFormat');
    CheckEqB(False, Image.HasAlpha, 'HasAlpha');
    CheckEqB(False, Image.AutoOrient, 'AutoOrient');
    CheckEqB(False, Image.Modified, 'Modified');
    Check(GetClass('TWICImage') = TWICImage, 'the class is registered for streaming (UIF-1351)');
    Check(GetModuleHandleW('windowscodecs.dll') = 0,
      'the imaging component is not loaded before the first operation (UIF-1351)');
  finally
    Image.Free;
  end;
end;

procedure CheckOne(const Name: string; W, H: Integer; const Format: string; Alpha: Boolean;
  TL, TR, BL, BR: Cardinal; Tolerance: Integer);
var
  Image: TWICImage;
  Counter: TChangeCounter;
  M: TMemoryStream;
begin
  Section(Name + ' (UIF-1342)');
  Image := TWICImage.Create;
  Counter := TChangeCounter.Create;
  M := FileBytes(Fixture(Name));
  try
    Image.OnChange := Counter.Changed;
    Image.LoadFromFile(Fixture(Name));
    CheckEqI(1, Counter.Count, 'from the file: OnChange fires once');
    CheckEqB(False, Image.Empty, 'from the file: not empty');
    CheckEqB(False, Image.Modified, 'from the file: Modified');
    CheckEqI(W, Image.Width, 'from the file: Width');
    CheckEqI(H, Image.Height, 'from the file: Height');
    CheckEqS(Format, FormatName(Image.ImageFormat), 'from the file: ImageFormat');
    CheckEqB(Alpha, Image.HasAlpha, 'from the file: HasAlpha');
    CheckBlocks(Image, TL, TR, BL, BR, Tolerance, 'from the file');
    Image.Assign(nil);
    CheckEqB(True, Image.Empty, 'emptied');
    Counter.Count := 0;
    Image.LoadFromStream(M);
    CheckEqI(1, Counter.Count, 'from the stream: OnChange fires once');
    CheckEqI(M.Size, M.Position, 'from the stream: the position is at the end');
    CheckEqI(W, Image.Width, 'from the stream: Width');
    CheckEqI(H, Image.Height, 'from the stream: Height');
    CheckEqS(Format, FormatName(Image.ImageFormat), 'from the stream: ImageFormat');
    CheckEqB(Alpha, Image.HasAlpha, 'from the stream: HasAlpha');
    CheckBlocks(Image, TL, TR, BL, BR, Tolerance, 'from the stream');
  finally
    M.Free;
    Counter.Free;
    Image.Free;
  end;
end;

procedure CheckPosition;
var
  Image: TWICImage;
  M, F: TMemoryStream;
  Junk: array[0..4] of Byte;
begin
  Section('the load starts at the position of the stream (UIF-1342)');
  Image := TWICImage.Create;
  M := TMemoryStream.Create;
  F := FileBytes(Fixture('alpha.png'));
  try
    FillChar(Junk, SizeOf(Junk), $55);
    M.WriteBuffer(Junk, SizeOf(Junk));
    M.WriteBuffer(F.Memory^, F.Size);
    M.Position := SizeOf(Junk);
    Image.LoadFromStream(M);
    CheckEqI(16, Image.Width, 'the image after five other bytes loads');
    CheckEqI(M.Size, M.Position, 'the position is at the end');
  finally
    F.Free;
    M.Free;
    Image.Free;
  end;
end;

procedure CheckFrames;
var
  Image: TWICImage;
  M: TMemoryStream;
begin
  Section('a file of two frames (UIF-1344)');
  Image := TWICImage.Create;
  M := TMemoryStream.Create;
  try
    M.WriteBuffer(TwoFrames, SizeOf(TwoFrames));
    M.Position := 0;
    Image.LoadFromStream(M);
    CheckEqI(1, Image.Width, 'Width');
    CheckEqI(1, Image.Height, 'Height');
    CheckEqS('wifGif', FormatName(Image.ImageFormat), 'ImageFormat');
    CheckEqS('FF0AC81E', QuadText(ImageQuad(Image, 0, 0)), 'the pixel is the one of frame 0');
  finally
    M.Free;
    Image.Free;
  end;
end;

procedure CheckResize;
var
  Image: TWICImage;
  Counter: TChangeCounter;
begin
  Section('the size cannot be set (UIF-1345)');
  Image := TWICImage.Create;
  Counter := TChangeCounter.Create;
  try
    Image.LoadFromFile(Fixture('plain.bmp'));
    Image.OnChange := Counter.Changed;
    Expect('EInvalidGraphicOperation', 'An image of this class cannot be resized');
    try
      Image.Width := 8;
      Check(False, 'Width raises');
    except
      on E: Exception do
        CheckRaises('Width raises', E.ClassName, E.Message);
    end;
    try
      Image.Height := 8;
      Check(False, 'Height raises');
    except
      on E: Exception do
        CheckRaises('Height raises', E.ClassName, E.Message);
    end;
    try
      Image.SetSize(16, 12);
      Check(False, 'SetSize raises, also with the size the image has');
    except
      on E: Exception do
        CheckRaises('SetSize raises, also with the size the image has', E.ClassName, E.Message);
    end;
    CheckEqI(16, Image.Width, 'Width unchanged');
    CheckEqI(12, Image.Height, 'Height unchanged');
    CheckEqI(0, Counter.Count, 'no OnChange');
  finally
    Counter.Free;
    Image.Free;
  end;
end;

begin
  CheckDefaults;
  CheckOne('alpha.png', 16, 12, 'wifPng', True, $FFFF0000, $FF00FF00, $80000080, $00000000, 0);
  CheckOne('plain.bmp', 16, 12, 'wifBmp', False, $FFFF0000, $FF00FF00, $FF0000FF, $FFFFFF00, 0);
  CheckOne('blocks.gif', 16, 12, 'wifGif', False, $FFFF0000, $FF00FF00, $FF0000FF, $FFFFFF00, 0);
  CheckOne('baseline.jpg', 32, 24, 'wifJpeg', False, $FFC82828, $FF28A03C, $FF3246C8, $FFE6DC50, 4);
  CheckOne('progressive.jpg', 32, 24, 'wifJpeg', False, $FFC82828, $FF28A03C, $FF3246C8, $FFE6DC50, 4);
  CheckPosition;
  CheckFrames;
  CheckResize;
  Done;
end.
