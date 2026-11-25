// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1357
// phase: 8
program Gfx_WicImageOrient;
{ U-150, wic-image.md section 4: AutoOrient. The fixture rotated.jpg is stored 32 x 24 with the orientation value 6:
  without AutoOrient it loads as stored, with it the picture is upright (24 x 32, the four blocks in their upright
  places, within 4 per channel). Setting the property on a loaded image decodes it again and fires OnChange; on an
  image without stored bytes it only stores the value. Each orientation value 1 to 8 (written into a copy of the
  file's bytes in memory) gives its own arrangement; a value outside that range, a JPEG without the value and a
  PNG are unchanged. The saved bytes are always those of the file. Assign copies the property. No desktop. }
uses
  Windows, SysUtils, Classes, Graphics, TestKit, WicTestKit;

const
  { the blocks of the stored picture: top left, top right, bottom left, bottom right }
  A = $FFC82828;
  B = $FF28A03C;
  C = $FF3246C8;
  D = $FFE6DC50;

{ the position of the orientation value in the bytes of rotated.jpg: the low byte of the 16-bit value of the
  metadata entry 274 (type 3, count 1; the block is stored high byte first); -1 when the entry is not found }
function OrientationOffset(M: TMemoryStream): Integer;
const
  Entry: array[0..7] of Byte = ($01, $12, $00, $03, $00, $00, $00, $01);
var
  P: PByte;
  I, J: Integer;
  Same: Boolean;
begin
  Result := -1;
  for I := 0 to M.Size - 11 do
  begin
    Same := True;
    for J := 0 to 7 do
    begin
      P := PByte(M.Memory);
      Inc(P, I + J);
      if P^ <> Entry[J] then
        Same := False;
    end;
    if Same then
    begin
      Result := I + 9;
      Exit;
    end;
  end;
end;

procedure CheckFixture;
var
  Image, Copy: TWICImage;
  Counter: TChangeCounter;
  Original, Saved: TMemoryStream;
  Bmp: TBitmap;
begin
  Section('the fixture with the orientation value 6 (UIF-1357)');
  Image := TWICImage.Create;
  Copy := TWICImage.Create;
  Counter := TChangeCounter.Create;
  Original := FileBytes(Fixture('rotated.jpg'));
  Saved := TMemoryStream.Create;
  Bmp := TBitmap.Create;
  try
    Image.LoadFromFile(Fixture('rotated.jpg'));
    CheckEqB(False, Image.AutoOrient, 'AutoOrient is False by default');
    CheckEqI(32, Image.Width, 'as stored: Width');
    CheckEqI(24, Image.Height, 'as stored: Height');
    CheckBlocks(Image, A, B, C, D, 4, 'as stored');

    Image.OnChange := Counter.Changed;
    Image.AutoOrient := True;
    CheckEqI(1, Counter.Count, 'setting AutoOrient on a loaded image fires OnChange once');
    CheckEqB(True, Image.AutoOrient, 'AutoOrient');
    CheckEqI(24, Image.Width, 'upright: Width');
    CheckEqI(32, Image.Height, 'upright: Height');
    CheckBlocks(Image, C, A, D, B, 4, 'upright');
    Check(Image.ImageFormat = wifJpeg, 'ImageFormat');
    Image.SaveToStream(Saved);
    Check(SameBytes(Original, Saved), 'the saved bytes are those of the file, the value included');

    Image.AutoOrient := True;
    CheckEqI(1, Counter.Count, 'setting the value it has does nothing');

    Copy.Assign(Image);
    CheckEqB(True, Copy.AutoOrient, 'Assign copies the property');
    CheckEqI(24, Copy.Width, 'the copy is upright');
    CheckBlocks(Copy, C, A, D, B, 4, 'the copy');

    Counter.Count := 0;
    Image.AutoOrient := False;
    CheckEqI(1, Counter.Count, 'setting it back fires OnChange once');
    CheckEqI(32, Image.Width, 'as stored again: Width');
    CheckEqI(24, Image.Height, 'as stored again: Height');
    CheckBlocks(Image, A, B, C, D, 4, 'as stored again');

    Image.Assign(nil);
    Image.AutoOrient := True;
    Counter.Count := 0;
    Image.LoadFromFile(Fixture('rotated.jpg'));
    CheckEqI(1, Counter.Count, 'a load with AutoOrient fires OnChange once');
    CheckEqI(24, Image.Width, 'a load with AutoOrient: Width');
    CheckEqI(32, Image.Height, 'a load with AutoOrient: Height');
    CheckBlocks(Image, C, A, D, B, 4, 'a load with AutoOrient');

    Bmp.SetSize(5, 4);
    Image.Assign(Bmp);
    Counter.Count := 0;
    Image.AutoOrient := False;
    CheckEqI(0, Counter.Count, 'an image without stored bytes: no OnChange');
    CheckEqB(False, Image.AutoOrient, 'an image without stored bytes: the value is stored');
    CheckEqI(5, Image.Width, 'an image without stored bytes: unchanged');
  finally
    Bmp.Free;
    Saved.Free;
    Original.Free;
    Counter.Free;
    Copy.Free;
    Image.Free;
  end;
end;

procedure CheckValue(Value, W, H: Integer; TL, TR, BL, BR: Cardinal);
var
  Image: TWICImage;
  M: TMemoryStream;
  P: PByte;
  At: Integer;
  Name: string;
begin
  Name := 'value ' + IntToStr(Value);
  Image := TWICImage.Create;
  M := FileBytes(Fixture('rotated.jpg'));
  try
    At := OrientationOffset(M);
    Check(At > 0, Name + ': the entry is in the file');
    P := PByte(M.Memory);
    Inc(P, At);
    P^ := Byte(Value);
    Image.AutoOrient := True;
    Image.LoadFromStream(M);
    CheckEqI(W, Image.Width, Name + ': Width');
    CheckEqI(H, Image.Height, Name + ': Height');
    CheckBlocks(Image, TL, TR, BL, BR, 4, Name);
  finally
    M.Free;
    Image.Free;
  end;
end;

procedure CheckValues;
begin
  Section('every orientation value (UIF-1357)');
  CheckValue(1, 32, 24, A, B, C, D);
  CheckValue(2, 32, 24, B, A, D, C);
  CheckValue(3, 32, 24, D, C, B, A);
  CheckValue(4, 32, 24, C, D, A, B);
  CheckValue(5, 24, 32, A, C, B, D);
  CheckValue(6, 24, 32, C, A, D, B);
  CheckValue(7, 24, 32, D, B, C, A);
  CheckValue(8, 24, 32, B, D, A, C);
  CheckValue(0, 32, 24, A, B, C, D);
  CheckValue(9, 32, 24, A, B, C, D);
end;

procedure CheckUntagged(const Name: string; W, H: Integer; TL, TR, BL, BR: Cardinal; Tolerance: Integer);
var
  Image: TWICImage;
  Counter: TChangeCounter;
begin
  Image := TWICImage.Create;
  Counter := TChangeCounter.Create;
  try
    Image.LoadFromFile(Fixture(Name));
    Image.OnChange := Counter.Changed;
    Image.AutoOrient := True;
    CheckEqI(1, Counter.Count, Name + ': decoded again');
    CheckEqI(W, Image.Width, Name + ': Width');
    CheckEqI(H, Image.Height, Name + ': Height');
    CheckBlocks(Image, TL, TR, BL, BR, Tolerance, Name);
  finally
    Counter.Free;
    Image.Free;
  end;
end;

begin
  CheckFixture;
  CheckValues;
  Section('images without the value are unchanged (UIF-1357)');
  CheckUntagged('baseline.jpg', 32, 24, A, B, C, D, 4);
  CheckUntagged('alpha.png', 16, 12, $FFFF0000, $FF00FF00, $80000080, $00000000, 0);
  CheckUntagged('blocks.gif', 16, 12, $FFFF0000, $FF00FF00, $FF0000FF, $FFFFFF00, 0);
  Done;
end.
