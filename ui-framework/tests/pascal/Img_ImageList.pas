// covers: UIF-565, UIF-566, UIF-567, UIF-568, UIF-569
// needs: desktop
program Img_ImageList;
{ U-012, graphics.md section 5: TImageList defaults and lazy handle; Add splits a wide bitmap (UIF-565);
  AddMasked leaves the source bitmap unchanged; Draw honours the mask, disabled drawing blends (UIF-566);
  Delete / Clear / index errors; changing Width clears; change links (UIF-567); GetBitmap; the defined
  binary property Bitmap: one 32-bit BMP strip with alpha = mask, absent when empty, read back from an
  inline .form together with DesignLeft/DesignTop (UIF-568, UIF-569). }
uses
  Windows, SysUtils, Classes, Graphics, ImgList, TestKit;

type
  TCaptureFiler = class(TFiler)
  public
    Names: string;
    Bytes: TMemoryStream;
    procedure DefineProperty(const Name: string; ReadData: TReaderProc; WriteData: TWriterProc;
      HasData: Boolean); override;
    procedure DefineBinaryProperty(const Name: string; ReadData, WriteData: TStreamProc;
      HasData: Boolean); override;
  end;

  TListAccess = class(TImageList)
  public
    procedure Define(Filer: TFiler);
  end;

  TRecorder = class
    Log: string;
    procedure ListChanged(Sender: TObject);
    procedure LinkChanged(Sender: TObject);
  end;

procedure TCaptureFiler.DefineProperty(const Name: string; ReadData: TReaderProc; WriteData: TWriterProc;
  HasData: Boolean);
begin
end;

procedure TCaptureFiler.DefineBinaryProperty(const Name: string; ReadData, WriteData: TStreamProc;
  HasData: Boolean);
begin
  Names := Names + Name + '(' + BoolToStr(HasData, True) + ');';
  if HasData and Assigned(WriteData) then
    WriteData(Bytes);
end;

procedure TListAccess.Define(Filer: TFiler);
begin
  DefineProperties(Filer);
end;

procedure TRecorder.ListChanged(Sender: TObject);
begin
  Log := Log + 'list;';
end;

procedure TRecorder.LinkChanged(Sender: TObject);
begin
  Log := Log + 'link;';
end;

{ W x 16 bitmap of Color with the pixel (1, 1) of each 16-pixel cell in clFuchsia }
function Strip(Cells: Integer; Color: TColor): TBitmap;
var
  I: Integer;
begin
  Result := TBitmap.Create;
  Result.SetSize(16 * Cells, 16);
  Result.Canvas.Brush.Color := Color;
  Result.Canvas.FillRect(Rect(0, 0, 16 * Cells, 16));
  for I := 0 to Cells - 1 do
    Result.Canvas.Pixels[16 * I + 1, 1] := clFuchsia;
end;

function Surface: TBitmap;
begin
  Result := TBitmap.Create;
  Result.SetSize(40, 20);
  Result.Canvas.Brush.Color := clLime;
  Result.Canvas.FillRect(Rect(0, 0, 40, 20));
end;

function MakeIcon: HICON;
var
  Info: TIconInfo;
  B: TBitmap;
begin
  B := Strip(1, clBlue);
  try
    FillChar(Info, SizeOf(Info), 0);
    Info.fIcon := True;
    Info.hbmMask := CreateBitmap(16, 16, 1, 1, nil);
    Info.hbmColor := B.Handle;
    Result := CreateIconIndirect(Info);
    DeleteObject(Info.hbmMask);
  finally
    B.Free;
  end;
end;

function StreamHex(M: TMemoryStream; From, Count: Integer): string;
var
  P: PByte;
  I: Integer;
begin
  Result := '';
  P := PByte(NativeUInt(M.Memory) + NativeUInt(From));
  for I := 1 to Count do
  begin
    Result := Result + IntToHex(P^, 2);
    Inc(P);
  end;
end;

procedure CheckDefaults;
var
  L: TImageList;
begin
  Section('defaults');
  L := TImageList.Create(nil);
  try
    CheckEqI(16, L.Width, 'Width');
    CheckEqI(16, L.Height, 'Height');
    Check(L.ColorDepth = cd32Bit, 'ColorDepth');
    CheckEqB(True, L.Masked, 'Masked');
    CheckEqI(clNone, L.BkColor, 'BkColor');
    Check(L.DrawingStyle = dsNormal, 'DrawingStyle');
    CheckEqI(0, L.Count, 'Count');
    CheckEqB(False, L.HandleAllocated, 'no handle before use');
    Check(L.Handle <> 0, 'Handle created on read');
    CheckEqB(True, L.HandleAllocated, 'HandleAllocated');
  finally
    L.Free;
  end;
end;

procedure CheckAddDraw;
var
  L: TImageList;
  B, S: TBitmap;
  Icon: TIcon;
begin
  Section('Add, AddMasked, AddIcon, Draw');
  L := TImageList.Create(nil);
  S := Surface;
  try
    B := Strip(3, clRed);
    try
      CheckEqI(0, L.Add(B, nil), 'Add returns the first index');
      CheckEqI(3, L.Count, 'a 48-pixel strip is split into 3 images (UIF-565)');
      CheckEqI(3, L.Add(B, nil), 'second Add starts at 3');
      CheckEqI(6, L.Count, 'Count after two strips');
    finally
      B.Free;
    end;
    L.Clear;
    CheckEqI(0, L.Count, 'Clear');
    B := Strip(1, clRed);
    try
      CheckEqI(0, L.AddMasked(B, clFuchsia), 'AddMasked index');
      CheckEqI(clFuchsia, B.Canvas.Pixels[1, 1], 'AddMasked leaves the source bitmap unchanged');
    finally
      B.Free;
    end;
    L.Draw(S.Canvas, 2, 2, 0);
    CheckEqI(clRed, S.Canvas.Pixels[2, 2], 'opaque pixel drawn');
    CheckEqI(clLime, S.Canvas.Pixels[3, 3], 'masked pixel transparent');
    CheckEqI(clLime, S.Canvas.Pixels[18, 18], 'outside the image');
    L.Draw(S.Canvas, 20, 2, 0, False);
    Check(S.Canvas.Pixels[20, 2] <> clRed, 'disabled drawing blends (UIF-566)');
    CheckEqI(clLime, S.Canvas.Pixels[21, 3], 'disabled drawing keeps the mask');
    Icon := TIcon.Create;
    try
      Icon.Handle := MakeIcon;
      CheckEqI(1, L.AddIcon(Icon), 'AddIcon index');
      CheckEqI(2, L.Count, 'Count after AddIcon');
    finally
      Icon.Free;
    end;
    L.Draw(S.Canvas, 2, 2, 1);
    CheckEqI(clBlue, S.Canvas.Pixels[7, 7], 'icon drawn');

    Section('GetBitmap');
    B := TBitmap.Create;
    try
      CheckEqB(True, L.GetBitmap(1, B), 'GetBitmap valid index');
      CheckEqI(16, B.Width, 'GetBitmap width');
      CheckEqI(16, B.Height, 'GetBitmap height');
      CheckEqI(clBlue, B.Canvas.Pixels[5, 5], 'GetBitmap pixels');
      CheckEqB(False, L.GetBitmap(7, B), 'GetBitmap invalid index');
    finally
      B.Free;
    end;

    Section('Delete');
    L.Delete(0);
    CheckEqI(1, L.Count, 'Delete');
    Expect('EInvalidOperation', 'Invalid ImageList Index');
    try
      L.Delete(5);
      Check(False, 'Delete out of range raises');
    except
      on E: Exception do
        CheckRaises('Delete out of range raises', E.ClassName, E.Message);
    end;
    L.Width := 24;
    CheckEqI(0, L.Count, 'changing Width clears the list');
    CheckEqI(24, L.Width, 'new Width');
  finally
    L.Free;
    S.Free;
  end;
end;

procedure CheckLinks;
var
  L: TImageList;
  Link, Other: TChangeLink;
  R: TRecorder;
  B: TBitmap;
begin
  Section('change links (UIF-567)');
  L := TImageList.Create(nil);
  R := TRecorder.Create;
  Link := TChangeLink.Create;
  Other := TChangeLink.Create;
  B := Strip(1, clRed);
  try
    L.OnChange := R.ListChanged;
    Link.OnChange := R.LinkChanged;
    L.RegisterChanges(Link);
    Check(Link.Sender = L, 'Sender set on register');
    L.Add(B, nil);
    CheckEqS('link;list;', R.Log, 'Add notifies links, then OnChange');
    R.Log := '';
    L.Height := 32;
    CheckEqS('link;list;', R.Log, 'size change notifies');
    R.Log := '';
    L.Height := 32;
    CheckEqS('', R.Log, 'unchanged size: nothing');
    L.UnRegisterChanges(Link);
    Check(Link.Sender = nil, 'Sender cleared on unregister');
    L.Clear;
    CheckEqS('list;', R.Log, 'unregistered link not notified');
    L.RegisterChanges(Link);
    L.RegisterChanges(Other);
    Other.Free;
    Other := nil;
    R.Log := '';
    L.Clear;
    CheckEqS('link;list;', R.Log, 'a freed link unregisters itself');
    L.OnChange := nil;
    L.Free;
    L := nil;
    Check(Link.Sender = nil, 'destroying the list clears Sender');
  finally
    L.Free;
    Other.Free;
    Link.Free;
    R.Free;
    B.Free;
  end;
end;

procedure CheckStreaming;
var
  L, M: TImageList;
  F: TCaptureFiler;
  B, S: TBitmap;
  Hex, Text: string;
  I: Integer;
begin
  Section('Bitmap defined property (UIF-568)');
  L := TImageList.Create(nil);
  F := TCaptureFiler.Create(nil, 0);
  F.Bytes := TMemoryStream.Create;
  M := TImageList.Create(nil);
  S := Surface;
  try
    TListAccess(L).Define(F);
    CheckEqS('Bitmap(False);', F.Names, 'empty list: Bitmap not stored');
    B := Strip(1, clRed);
    try
      L.AddMasked(B, clFuchsia);
    finally
      B.Free;
    end;
    B := Strip(1, clBlue);
    try
      L.AddMasked(B, clFuchsia);
    finally
      B.Free;
    end;
    F.Names := '';
    TListAccess(L).Define(F);
    CheckEqS('Bitmap(True);', F.Names, 'Bitmap stored');
    CheckEqI(54 + 32 * 16 * 4, F.Bytes.Size, 'one 32 x 16 32-bit BMP');
    CheckEqS('424D', StreamHex(F.Bytes, 0, 2), 'BMP signature');
    CheckEqS('20000000' + '10000000' + '0100' + '2000', StreamHex(F.Bytes, 18, 12), 'width 32, height 16, 32 bits');
    { bottom-up: file row 14 is image row 1; pixel (1,1) of image 0 is transparent, (0,1) opaque red }
    CheckEqS('0000FFFF', StreamHex(F.Bytes, 54 + 14 * 128 + 0 * 4, 4), 'opaque pixel: colour, alpha FF');
    CheckEqS('00', StreamHex(F.Bytes, 54 + 14 * 128 + 1 * 4 + 3, 1), 'transparent pixel: alpha 0');
    CheckEqS('FF0000FF', StreamHex(F.Bytes, 54 + 14 * 128 + 16 * 4, 4), 'second image opaque blue');

    Hex := StreamHex(F.Bytes, 0, F.Bytes.Size);
    Text := '';
    for I := 1 to Length(Hex) do
    begin
      Text := Text + Hex[I];
      if I mod 64 = 0 then
        Text := Text + #10'    ';
    end;
    ReadComponentFromText('object M: TImageList'#10'  Height = 16'#10'  Width = 16'#10 +
      '  Bitmap = {'#10'    ' + Text + #10'  }'#10'  DesignLeft = 24'#10'  DesignTop = 40'#10'end'#10, M, 'M.form');
    CheckEqI(2, M.Count, 'read back: two images');
    M.Draw(S.Canvas, 0, 0, 0);
    M.Draw(S.Canvas, 20, 0, 1);
    CheckEqI(clRed, S.Canvas.Pixels[0, 0], 'read back image 0 opaque');
    CheckEqI(clLime, S.Canvas.Pixels[1, 1], 'read back image 0 transparent pixel');
    CheckEqI(clBlue, S.Canvas.Pixels[20, 0], 'read back image 1 opaque');
    CheckEqI(clLime, S.Canvas.Pixels[21, 1], 'read back image 1 transparent pixel');
    CheckEqI(24, M.DesignInfo and $FFFF, 'DesignLeft (UIF-569)');
    CheckEqI(40, M.DesignInfo shr 16, 'DesignTop (UIF-569)');
    Check(GetClass('TImageList') = TImageList, 'TImageList registered (UIF-002)');
  finally
    F.Bytes.Free;
    F.Free;
    L.Free;
    M.Free;
    S.Free;
  end;
end;

begin
  CheckDefaults;
  CheckAddDraw;
  CheckLinks;
  CheckStreaming;
  Done;
end.
