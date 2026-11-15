// covers: UIF-550, UIF-555
// opc: -U ..\..\src
// opc-run: tempdir
program Gfx_Picture;
{ U-011, graphics.md sections 4.2 and 4.3: TIcon (Handle ownership, size from GetIconInfo, DrawIconEx,
  LoadFromFile of an .ico, streaming not supported); TPicture (owned copy of the graphic, Bitmap/Icon
  replace a graphic of another class, Width/Height, Assign from picture/graphic/nil, OnChange, LoadFromFile
  through the file-format registry with later registrations winning, unknown extension error); the
  defined binary property Data: byte N, N bytes class name, then the graphic's SaveToStream bytes, not
  stored when empty, read back from an inline .form. }
uses
  Windows, SysUtils, Classes, Graphics, TestKit;

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

  { calls the protected DefineProperties from a method of a descendant (classes-interfaces.md visibility) }
  TPictureAccess = class(TPicture)
  public
    procedure Define(Filer: TFiler);
  end;

  TPicHolder = class(TComponent)
  private
    FPicture: TPicture;
    procedure SetPicture(Value: TPicture);
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  published
    property Picture: TPicture read FPicture write SetPicture;
  end;

  TMyBitmap = class(TBitmap);

  TCounter = class
    Count: Integer;
    procedure Changed(Sender: TObject);
  end;

var
  TempDir: string;

procedure TCaptureFiler.DefineProperty(const Name: string; ReadData: TReaderProc; WriteData: TWriterProc;
  HasData: Boolean);
begin
  Names := Names + Name + ';';
end;

procedure TCaptureFiler.DefineBinaryProperty(const Name: string; ReadData, WriteData: TStreamProc;
  HasData: Boolean);
begin
  Names := Names + Name + '(' + BoolToStr(HasData, True) + ');';
  if HasData and Assigned(WriteData) then
    WriteData(Bytes);
end;

constructor TPicHolder.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FPicture := TPicture.Create;
end;

destructor TPicHolder.Destroy;
begin
  FPicture.Free;
  inherited Destroy;
end;

procedure TPicHolder.SetPicture(Value: TPicture);
begin
  FPicture.Assign(Value);
end;

procedure TPictureAccess.Define(Filer: TFiler);
begin
  DefineProperties(Filer);
end;

procedure TCounter.Changed(Sender: TObject);
begin
  Inc(Count);
end;

function StreamHex(M: TMemoryStream): string;
var
  P: PByte;
  I: Integer;
begin
  Result := '';
  P := PByte(M.Memory);
  for I := 0 to M.Size - 1 do
  begin
    Result := Result + IntToHex(P^, 2);
    Inc(P);
  end;
end;

{ a 16 x 16 icon whose colour bitmap is Color and whose mask is all opaque }
function MakeIcon(Color: TColor): HICON;
var
  Info: TIconInfo;
  B: TBitmap;
begin
  B := TBitmap.Create;
  try
    B.SetSize(16, 16);
    B.Canvas.Brush.Color := Color;
    B.Canvas.FillRect(Rect(0, 0, 16, 16));
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

function IconAlive(H: HICON): Boolean;
var
  Info: TIconInfo;
begin
  FillChar(Info, SizeOf(Info), 0);
  Result := GetIconInfo(H, Info);
  if Result then
  begin
    DeleteObject(Info.hbmMask);
    DeleteObject(Info.hbmColor);
  end;
end;

{ a 16 x 16 32-bit .ico file, every pixel opaque blue }
procedure WriteIcoFile(const FileName: string);
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
  F := TFileStream.Create(FileName, fmCreate);
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

procedure CheckIcon;
var
  Icon: TIcon;
  H: HICON;
  Dest: TBitmap;
begin
  Section('TIcon (UIF-550)');
  Icon := TIcon.Create;
  Dest := TBitmap.Create;
  try
    CheckEqB(True, Icon.Empty, 'empty icon');
    CheckEqI(0, Icon.Width, 'empty width');
    H := MakeIcon(clRed);
    Icon.Handle := H;
    CheckEqB(False, Icon.Empty, 'not empty');
    CheckEqI(16, Icon.Width, 'Width from GetIconInfo');
    CheckEqI(16, Icon.Height, 'Height from GetIconInfo');
    Dest.SetSize(20, 20);
    Dest.Canvas.Brush.Color := clWhite;
    Dest.Canvas.FillRect(Rect(0, 0, 20, 20));
    Dest.Canvas.Draw(2, 2, Icon);
    CheckEqI(clRed, Dest.Canvas.Pixels[2, 2], 'DrawIconEx draws the icon');
    CheckEqI(clRed, Dest.Canvas.Pixels[17, 17], 'icon bottom-right');
    CheckEqI(clWhite, Dest.Canvas.Pixels[18, 18], 'outside the icon');
    Expect('EInvalidGraphicOperation', 'Icon streaming is not supported');
    try
      Icon.SaveToStream(nil);
      Check(False, 'SaveToStream raises');
    except
      on E: Exception do
        CheckRaises('SaveToStream raises', E.ClassName, E.Message);
    end;
    Expect('EInvalidGraphicOperation', 'Icon streaming is not supported');
    try
      Icon.LoadFromStream(nil);
      Check(False, 'LoadFromStream raises');
    except
      on E: Exception do
        CheckRaises('LoadFromStream raises', E.ClassName, E.Message);
    end;
    Icon.Handle := MakeIcon(clBlue);
    CheckEqB(False, IconAlive(H), 'replaced icon destroyed');
    H := Icon.Handle;
  finally
    Icon.Free;
    Dest.Free;
  end;
  CheckEqB(False, IconAlive(H), 'owned icon destroyed with the object');

  Icon := TIcon.Create;
  try
    WriteIcoFile(TempDir + '\blue.ico');
    Icon.LoadFromFile(TempDir + '\blue.ico');
    CheckEqB(False, Icon.Empty, 'LoadFromFile .ico');
    Check(Icon.Width > 0, 'loaded icon has a width');
    CheckEqI(Icon.Width, Icon.Height, 'loaded icon is square');
  finally
    Icon.Free;
  end;
end;

procedure CheckPictureBasics;
var
  P, Q: TPicture;
  B: TBitmap;
  Counter: TCounter;
  Icon: TIcon;
begin
  Section('TPicture');
  P := TPicture.Create;
  Q := TPicture.Create;
  B := TBitmap.Create;
  Counter := TCounter.Create;
  try
    Check(P.Graphic = nil, 'empty picture has no graphic');
    CheckEqI(0, P.Width, 'empty Width');
    CheckEqI(0, P.Height, 'empty Height');
    P.OnChange := Counter.Changed;
    B.SetSize(3, 2);
    B.Canvas.Pixels[1, 1] := clRed;
    P.Graphic := B;
    CheckEqI(1, Counter.Count, 'assigning a graphic fires OnChange');
    Check(P.Graphic <> B, 'the picture owns a copy');
    Check(P.Graphic is TBitmap, 'copy of the same class');
    CheckEqI(3, P.Width, 'Width of the graphic');
    CheckEqI(2, P.Height, 'Height of the graphic');
    CheckEqI(clRed, P.Bitmap.Canvas.Pixels[1, 1], 'copied pixels');
    Check(P.Bitmap = P.Graphic, 'Bitmap returns the bitmap graphic');
    P.Bitmap.Canvas.Pixels[0, 0] := clBlue;
    Check(Counter.Count >= 2, 'a change of the graphic fires the picture OnChange');
    Icon := P.Icon;
    Check(P.Graphic = Icon, 'Icon replaces the bitmap');
    CheckEqB(True, Icon.Empty, 'with a new empty icon');
    Check(P.Bitmap <> nil, 'Bitmap replaces the icon');
    CheckEqB(True, P.Bitmap.Empty, 'with a new empty bitmap');
    P.Graphic := B;
    Q.Assign(P);
    Check(Q.Graphic <> P.Graphic, 'Assign(TPicture) copies');
    CheckEqI(clRed, Q.Bitmap.Canvas.Pixels[1, 1], 'Assign(TPicture) pixels');
    Q.Assign(nil);
    Check(Q.Graphic = nil, 'Assign(nil) clears');
    Q.Assign(B);
    CheckEqI(3, Q.Width, 'Assign(TGraphic)');
    P.Graphic := nil;
    Check(P.Graphic = nil, 'Graphic := nil clears');
  finally
    P.Free;
    Q.Free;
    B.Free;
    Counter.Free;
  end;
end;

procedure CheckFiles;
var
  P: TPicture;
  B: TBitmap;
begin
  Section('file-format registry');
  P := TPicture.Create;
  B := TBitmap.Create;
  try
    B.SetSize(2, 2);
    B.Canvas.Pixels[0, 0] := clLime;
    B.SaveToFile(TempDir + '\img.bmp');
    P.LoadFromFile(TempDir + '\img.bmp');
    Check(P.Graphic is TBitmap, '.bmp loads a TBitmap');
    CheckEqI(clLime, P.Bitmap.Canvas.Pixels[0, 0], 'loaded pixels');
    B.SaveToFile(TempDir + '\IMG2.BMP');
    P.LoadFromFile(TempDir + '\IMG2.BMP');
    Check(P.Graphic is TBitmap, 'extension is case-insensitive');
    P.LoadFromFile(TempDir + '\blue.ico');
    Check(P.Graphic is TIcon, '.ico loads a TIcon');
    try
      P.SaveToFile(TempDir + '\copy.ico');
      Check(False, 'saving an icon picture raises');
    except
      on E: Exception do
        CheckEqS('EInvalidGraphicOperation', E.ClassName, 'saving an icon picture raises');
    end;
    Expect('EInvalidGraphic', 'Unknown picture file extension (.xyz)');
    try
      P.LoadFromFile(TempDir + '\a.xyz');
      Check(False, 'unknown extension raises');
    except
      on E: Exception do
        CheckRaises('unknown extension raises', E.ClassName, E.Message);
    end;
    TPicture.RegisterFileFormat('bmp', 'My bitmaps', TMyBitmap);
    P.LoadFromFile(TempDir + '\img.bmp');
    CheckEqS('TMyBitmap', P.Graphic.ClassName, 'later registration wins');
    TPicture.RegisterFileFormat('bmp', 'Bitmaps', TBitmap);
    P.Bitmap.SetSize(1, 1);
    P.SaveToFile(TempDir + '\saved.bmp');
    B.LoadFromFile(TempDir + '\saved.bmp');
    CheckEqI(1, B.Width, 'SaveToFile writes the graphic');
  finally
    P.Free;
    B.Free;
  end;
end;

procedure CheckData;
var
  P: TPicture;
  F: TCaptureFiler;
  H: TPicHolder;
  Hex, Text: string;
  I: Integer;
begin
  Section('Data defined property (UIF-555)');
  P := TPicture.Create;
  F := TCaptureFiler.Create(nil, 0);
  F.Bytes := TMemoryStream.Create;
  try
    TPictureAccess(P).Define(F);
    CheckEqS('Data(False);', F.Names, 'empty picture: Data not stored');
    P.Bitmap.SetSize(1, 1);
    P.Bitmap.Canvas.Pixels[0, 0] := TColor($332211);
    F.Names := '';
    TPictureAccess(P).Define(F);
    CheckEqS('Data(True);', F.Names, 'non-empty picture: Data stored');
    Hex := StreamHex(F.Bytes);
    CheckEqS('07' + '544269746D6170' +
      '424D3A000000000000003600000028000000010000000100000001002000000000000400000000000000000000000000000000000000' +
      '33221100', Hex, 'N, class name, SaveToStream bytes');
  finally
    F.Bytes.Free;
    F.Free;
    P.Free;
  end;

  H := TPicHolder.Create(nil);
  try
    Text := '';
    for I := 1 to Length(Hex) do
    begin
      Text := Text + Hex[I];
      if I mod 64 = 0 then
        Text := Text + #10'    ';
    end;
    ReadComponentFromText('object H: TPicHolder'#10'  Picture.Data = {'#10'    ' + Text + #10'  }'#10'end'#10, H, 'H.form');
    Check(H.Picture.Graphic is TBitmap, 'Data read back creates the bitmap');
    CheckEqI(1, H.Picture.Width, 'read back width');
    CheckEqI($332211, H.Picture.Bitmap.Canvas.Pixels[0, 0], 'read back pixel');
    Expect('EReadError', 'Invalid value for property Data');
    try
      ReadComponentFromText('object H: TPicHolder'#10'  Picture.Data = {'#10'    044E6F6E65'#10'  }'#10'end'#10, H, 'H.form');
      Check(False, 'unknown class name raises');
    except
      on E: Exception do
        CheckRaises('unknown class name raises', E.ClassName, E.Message);
    end;
  finally
    H.Free;
  end;
end;

begin
  TempDir := ParamStr(1);
  CheckIcon;
  CheckPictureBasics;
  CheckFiles;
  CheckData;
  Done;
end.
