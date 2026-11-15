// phase: 4
// covers: RTL-422, RTL-423
// opc-run: tempdir
program Cls_StringsFiles;
{ TStrings load/save with encodings (classes.md section 5.1): saving uses the parameter, else the encoding of the last
  load, else DefaultEncoding, and writes the preamble only with WriteBOM (RTL-423); loading detects a UTF-8 /
  UTF-16LE / UTF-16BE BOM when no encoding is given, else uses DefaultEncoding, skips the preamble of an explicit
  encoding, reads from the current stream position and remembers the encoding (RTL-422); open and create failures
  raise EFOpenError / EFCreateError with the full path and the system message. }
uses
  Windows, SysUtils, Classes, TestKit;

{$POINTERMATH ON}

var
  Dir: string;

function Hex(const FileName: string): string;
var
  M: TMemoryStream;
  I: Integer;
begin
  M := TMemoryStream.Create;
  try
    M.LoadFromFile(FileName);
    Result := '';
    for I := 0 to Integer(M.Size) - 1 do
      Result := Result + IntToHex((PByte(M.Memory) + I)^, 2);
  finally
    M.Free;
  end;
end;

procedure WriteRaw(const FileName: string; const Bytes: array of Byte);
var
  F: TFileStream;
begin
  F := TFileStream.Create(FileName, fmCreate);
  try
    if Length(Bytes) > 0 then
      F.WriteBuffer(Bytes[0], Length(Bytes));
  finally
    F.Free;
  end;
end;

function Lines(L: TStrings): string;
var
  I: Integer;
begin
  Result := IntToStr(L.Count) + ':';
  for I := 0 to L.Count - 1 do
    Result := Result + '[' + L[I] + ']';
end;

var
  L: TStringList;
  F: TFileStream;
  M: TMemoryStream;
  Msg: string;
  XX: array[0..1] of Byte;
begin
  Dir := IncludeTrailingPathDelimiter(ParamStr(1));
  L := TStringList.Create;

  Section('save (RTL-423)');
  L.Add('a');
  L.Add('b'#$00E9);
  L.SaveToFile(Dir + 'u8.txt');
  CheckEqS('610D0A62C3A90D0A', Hex(Dir + 'u8.txt'), 'default UTF-8 without BOM');
  L.WriteBOM := True;
  L.SaveToFile(Dir + 'u8bom.txt');
  CheckEqS('EFBBBF610D0A62C3A90D0A', Hex(Dir + 'u8bom.txt'), 'WriteBOM writes the preamble');
  L.SaveToFile(Dir + 'u16.txt', TEncoding.Unicode);
  CheckEqS('FFFE61000D000A006200E9000D000A00', Hex(Dir + 'u16.txt'), 'explicit UTF-16LE');
  L.WriteBOM := False;
  L.SaveToFile(Dir + 'u16be.txt', TEncoding.BigEndianUnicode);
  CheckEqS('0061000D000A006200E9000D000A', Hex(Dir + 'u16be.txt'), 'explicit UTF-16BE without BOM');
  Check(L.Encoding = nil, 'saving does not set Encoding');
  L.DefaultEncoding := TEncoding.Unicode;
  L.SaveToFile(Dir + 'def.txt');
  CheckEqS('61000D000A006200E9000D000A00', Hex(Dir + 'def.txt'), 'DefaultEncoding');
  L.DefaultEncoding := nil;
  Check(L.DefaultEncoding = TEncoding.UTF8, 'DefaultEncoding nil resets to UTF-8');

  Section('load (RTL-422)');
  L.Clear;
  L.LoadFromFile(Dir + 'u16.txt');
  CheckEqS('2:[a][b'#$00E9']', Lines(L), 'UTF-16LE BOM detected');
  Check(L.Encoding = TEncoding.Unicode, 'Encoding remembers UTF-16LE');
  L.SaveToFile(Dir + 'again.txt');
  CheckEqS('61000D000A006200E9000D000A00', Hex(Dir + 'again.txt'), 'save uses the loaded encoding');
  L.LoadFromFile(Dir + 'u8bom.txt');
  CheckEqS('2:[a][b'#$00E9']', Lines(L), 'UTF-8 BOM detected and skipped');
  Check(L.Encoding = TEncoding.UTF8, 'Encoding remembers UTF-8');
  WriteRaw(Dir + 'be.txt', [$FE, $FF, $00, $78, $00, $0A, $00, $79]);
  L.LoadFromFile(Dir + 'be.txt');
  CheckEqS('2:[x][y]', Lines(L), 'UTF-16BE BOM detected');
  Check(L.Encoding = TEncoding.BigEndianUnicode, 'Encoding remembers UTF-16BE');
  L.LoadFromFile(Dir + 'u8.txt');
  CheckEqS('2:[a][b'#$00E9']', Lines(L), 'no BOM: DefaultEncoding UTF-8');
  Check(L.Encoding = TEncoding.UTF8, 'Encoding is the default');
  L.DefaultEncoding := TEncoding.Unicode;
  L.LoadFromFile(Dir + 'def.txt');
  CheckEqS('2:[a][b'#$00E9']', Lines(L), 'no BOM: DefaultEncoding UTF-16LE');
  L.DefaultEncoding := TEncoding.UTF8;
  L.LoadFromFile(Dir + 'u16.txt', TEncoding.Unicode);
  CheckEqS('2:[a][b'#$00E9']', Lines(L), 'explicit encoding skips its preamble');
  L.LoadFromFile(Dir + 'u16be.txt', TEncoding.BigEndianUnicode);
  CheckEqS('2:[a][b'#$00E9']', Lines(L), 'explicit encoding without BOM');
  L.LoadFromFile(Dir + 'u8bom.txt', TEncoding.ASCII);
  CheckEqI(2, L.Count, 'explicit encoding does not detect another BOM');
  Check(L.Encoding = TEncoding.ASCII, 'explicit encoding is remembered');
  WriteRaw(Dir + 'empty.txt', []);
  L.LoadFromFile(Dir + 'empty.txt');
  CheckEqS('0:', Lines(L), 'empty file');

  Section('streams');
  M := TMemoryStream.Create;
  L.Text := 'p'#10'q';
  L.SaveToStream(M);
  CheckEqI(6, M.Size, 'SaveToStream');
  M.Position := 3;
  L.LoadFromStream(M);
  CheckEqS('1:[q]', Lines(L), 'LoadFromStream reads from the current position');
  M.Position := 0;
  L.LoadFromStream(M, TEncoding.UTF8);
  CheckEqS('2:[p][q]', Lines(L), 'LoadFromStream with an encoding');
  M.Free;
  F := TFileStream.Create(Dir + 'tail.bin', fmCreate);
  XX[0] := $58;
  XX[1] := $58;
  F.WriteBuffer(XX, 2);
  L.SaveToStream(F, TEncoding.Unicode);
  F.Free;
  CheckEqS('585870000D000A0071000D000A00', Hex(Dir + 'tail.bin'), 'SaveToStream writes at the position');

  Section('errors');
  Msg := '';
  try
    L.LoadFromFile(Dir + 'missing.txt');
  except
    on E: EFOpenError do
      Msg := E.Message;
  end;
  CheckEqS('Cannot open file "' + Dir + 'missing.txt". ' + SysErrorMessage(ERROR_FILE_NOT_FOUND), Msg, 'open failure');
  Msg := '';
  try
    L.SaveToFile(Dir + 'no\dir\x.txt');
  except
    on E: EFCreateError do
      Msg := E.Message;
  end;
  CheckEqS('Cannot create file "' + Dir + 'no\dir\x.txt". ' + SysErrorMessage(ERROR_PATH_NOT_FOUND), Msg,
    'create failure');
  L.Free;
  Done;
end.
