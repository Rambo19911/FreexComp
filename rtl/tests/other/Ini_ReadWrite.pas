// phase: 7
// covers: RTL-680, RTL-681, RTL-683
// opc-run: tempdir
program Ini_ReadWrite;
{ IniFiles (other-units.md section 9): a missing file is empty; typed Read*/Write* (integers with $hex, Bool as 1/0
  and any non-zero integer, floats and dates in invariant text independent of FormatSettings); quotes removed on
  read; ASCII case-insensitive names unless CaseSensitive (RTL-683); SectionExists, ValueExists, ReadSection(s),
  ReadSectionValues, EraseSection, DeleteKey; TMemIniFile writes at UpdateFile or with AutoSave at Destroy,
  TIniFile at every Write; Clear, GetStrings/SetStrings, Rename; the whole file is rewritten with CRLF, a write
  failure raises EIniFileException (RTL-680); BOM detection and preservation (RTL-681). }
uses
  SysUtils, Classes, IniFiles, TestKit;

{$POINTERMATH ON}

var
  Dir: string;

function FileText(const FileName: string): string;
var
  L: TStringList;
begin
  L := TStringList.Create;
  try
    L.LoadFromFile(FileName);
    L.LineBreak := '|';
    Result := L.Text;
  finally
    L.Free;
  end;
end;

function Hex(const FileName: string; Count: Integer): string;
var
  M: TMemoryStream;
  I: Integer;
begin
  M := TMemoryStream.Create;
  try
    M.LoadFromFile(FileName);
    Result := '';
    for I := 0 to Count - 1 do
      if I < M.Size then
        Result := Result + IntToHex((PByte(M.Memory) + I)^, 2);
  finally
    M.Free;
  end;
end;

function Raw(const FileName: string): string;
var
  M: TMemoryStream;
  I: Integer;
begin
  M := TMemoryStream.Create;
  try
    M.LoadFromFile(FileName);
    Result := '';
    for I := 0 to Integer(M.Size) - 1 do
      Result := Result + Char((PByte(M.Memory) + I)^);
  finally
    M.Free;
  end;
end;

procedure WriteText(const FileName, Text: string; Enc: TEncoding; BOM: Boolean);
var
  L: TStringList;
begin
  L := TStringList.Create;
  try
    L.Text := Text;
    L.WriteBOM := BOM;
    L.SaveToFile(FileName, Enc);
  finally
    L.Free;
  end;
end;

function Joined(L: TStrings): string;
begin
  L.LineBreak := ',';
  Result := L.Text;
end;

var
  Ini: TMemIniFile;
  F: TIniFile;
  L: TStringList;
  Msg: string;
  D: TDateTime;
begin
  Dir := IncludeTrailingPathDelimiter(ParamStr(1));
  L := TStringList.Create;

  Section('missing file and typed values');
  Ini := TMemIniFile.Create(Dir + 'a.ini');
  CheckEqS(Dir + 'a.ini', Ini.FileName, 'FileName');
  CheckEqS('def', Ini.ReadString('S', 'X', 'def'), 'missing file is empty');
  CheckEqB(False, Ini.SectionExists('S'), 'no section');
  Ini.WriteString('Main', 'Name', 'Ann');
  Ini.WriteInteger('Main', 'Count', -12);
  Ini.WriteInt64('Main', 'Big', 5000000000);
  Ini.WriteBool('Main', 'On', True);
  Ini.WriteBool('Main', 'Off', False);
  FormatSettings.DecimalSeparator := ',';
  Ini.WriteFloat('Main', 'Ratio', 2.5);
  D := EncodeDate(2026, 11, 15) + EncodeTime(13, 4, 5, 6);
  Ini.WriteDate('Dates', 'D', D);
  Ini.WriteTime('Dates', 'T', D);
  Ini.WriteDateTime('Dates', 'DT', D);
  CheckEqB(False, FileExists(Dir + 'a.ini'), 'TMemIniFile writes nothing before UpdateFile');
  Ini.UpdateFile;
  CheckEqS('[Main]|Name=Ann|Count=-12|Big=5000000000|On=1|Off=0|Ratio=2.5||[Dates]|D=2026-11-15|T=13:04:05.006|' +
    'DT=2026-11-15T13:04:05.006|', FileText(Dir + 'a.ini'), 'file text, invariant floats and ISO dates');
  CheckEqS('[Main]'#13#10'Name=Ann'#13#10, Copy(Raw(Dir + 'a.ini'), 1, 18), 'CRLF line ends');
  CheckEqS('5B', Hex(Dir + 'a.ini', 1), 'UTF-8 without BOM');
  Ini.Free;

  Section('reading');
  WriteText(Dir + 'b.ini', '[Main]'#10'Name = Ann '#10'Count=$1F'#10'Bad=x'#10'Flag=5'#10'Zero=0'#10'Ratio=0.25'#10 +
    'Q1="x y"'#10'Q2=''q'''#10'Q3="open'#10'Big=-5000000000'#10'[Dates]'#10'D=2026-11-15'#10'T=13:04:05.006'#10 +
    'DT=2026-11-15T13:04:05.006'#10, TEncoding.UTF8, False);
  Ini := TMemIniFile.Create(Dir + 'b.ini');
  CheckEqS('Ann', Ini.ReadString('main', 'NAME', ''), 'trimmed, case-insensitive');
  CheckEqI(31, Ini.ReadInteger('Main', 'Count', 0), '$hex integer');
  CheckEqI(7, Ini.ReadInteger('Main', 'Bad', 7), 'invalid integer gives the default');
  CheckEqI(9, Ini.ReadInteger('Main', 'Missing', 9), 'missing integer gives the default');
  CheckEqS('-5000000000', IntToStr(Ini.ReadInt64('Main', 'Big', 0)), 'Int64');
  CheckEqB(True, Ini.ReadBool('Main', 'Flag', False), 'non-zero is True');
  CheckEqB(False, Ini.ReadBool('Main', 'Zero', True), 'zero is False');
  CheckEqB(True, Ini.ReadBool('Main', 'Missing', True), 'missing Bool gives the default');
  CheckEqF(0.25, Ini.ReadFloat('Main', 'Ratio', 0), 0, 'invariant float with a comma decimal separator set');
  CheckEqF(1.5, Ini.ReadFloat('Main', 'Bad', 1.5), 0, 'invalid float gives the default');
  CheckEqS('x y', Ini.ReadString('Main', 'Q1', ''), 'double quotes removed');
  CheckEqS('q', Ini.ReadString('Main', 'Q2', ''), 'single quotes removed');
  CheckEqS('"open', Ini.ReadString('Main', 'Q3', ''), 'an unpaired quote stays');
  CheckEqF(EncodeDate(2026, 11, 15), Ini.ReadDate('Dates', 'D', 0), 0, 'ReadDate');
  CheckEqF(EncodeTime(13, 4, 5, 6), Ini.ReadTime('Dates', 'T', 0), 4, 'ReadTime');
  CheckEqF(D, Ini.ReadDateTime('Dates', 'DT', 0), 4, 'ReadDateTime');
  CheckEqF(1, Ini.ReadDate('Dates', 'T', 1), 0, 'invalid date gives the default');

  Section('sections and keys');
  CheckEqB(True, Ini.SectionExists('DATES'), 'SectionExists');
  CheckEqB(True, Ini.ValueExists('Dates', 'd'), 'ValueExists');
  CheckEqB(False, Ini.ValueExists('Dates', 'Nope'), 'ValueExists absent');
  Ini.ReadSections(L);
  CheckEqS('Main,Dates,', Joined(L), 'ReadSections');
  Ini.ReadSection('Dates', L);
  CheckEqS('D,T,DT,', Joined(L), 'ReadSection');
  Ini.ReadSectionValues('Main', L);
  CheckEqS('Name=Ann', L[0], 'ReadSectionValues first');
  CheckEqS('Q1=x y', L[6], 'ReadSectionValues dequoted');
  Ini.DeleteKey('Dates', 'T');
  CheckEqB(False, Ini.ValueExists('Dates', 'T'), 'DeleteKey');
  Ini.EraseSection('dates');
  CheckEqB(False, Ini.SectionExists('Dates'), 'EraseSection');
  Ini.WriteString('main', 'name', 'Bob');
  Ini.UpdateFile;
  CheckEqS('[Main]|Name=Bob|Count=$1F|', Copy(FileText(Dir + 'b.ini'), 1, 26), 'a write updates the existing line, keeping its identifier');
  Ini.Free;

  Section('CaseSensitive');
  Ini := TMemIniFile.Create(Dir + 'b.ini');
  Ini.CaseSensitive := True;
  CheckEqS('', Ini.ReadString('Main', 'NAME', ''), 'identifier case matters');
  CheckEqS('', Ini.ReadString('MAIN', 'Name', ''), 'section case matters');
  CheckEqS('Bob', Ini.ReadString('Main', 'Name', ''), 'exact match');
  Ini.Free;

  Section('TIniFile, AutoSave, strings');
  F := TIniFile.Create(Dir + 'c.ini');
  F.WriteString('S', 'K', 'V');
  CheckEqS('[S]|K=V|', FileText(Dir + 'c.ini'), 'TIniFile writes at once');
  F.DeleteKey('S', 'K');
  CheckEqS('[S]|', FileText(Dir + 'c.ini'), 'DeleteKey writes at once');
  F.Free;
  Ini := TMemIniFile.Create(Dir + 'd.ini');
  Ini.AutoSave := True;
  Ini.WriteString('S', 'K', 'auto');
  Ini.Free;
  CheckEqS('[S]|K=auto|', FileText(Dir + 'd.ini'), 'AutoSave at Destroy');
  Ini := TMemIniFile.Create(Dir + 'd.ini');
  Ini.GetStrings(L);
  CheckEqS('[S],K=auto,', Joined(L), 'GetStrings');
  L.Clear;
  L.Add('[N]');
  L.Add('x=1');
  Ini.SetStrings(L);
  CheckEqI(1, Ini.ReadInteger('N', 'x', 0), 'SetStrings');
  Ini.Clear;
  CheckEqB(False, Ini.SectionExists('N'), 'Clear');
  Ini.Rename(Dir + 'c.ini', True);
  CheckEqS(Dir + 'c.ini', Ini.FileName, 'Rename');
  CheckEqB(True, Ini.SectionExists('S'), 'Rename with Reload');
  Ini.Free;

  Section('write failure (RTL-680)');
  Ini := TMemIniFile.Create(Dir + 'no\such\dir\e.ini');
  Ini.WriteString('S', 'K', 'V');
  Msg := '';
  try
    Ini.UpdateFile;
  except
    on E: EIniFileException do
      Msg := E.Message;
  end;
  CheckEqS('Unable to write to ' + Dir + 'no\such\dir\e.ini', Msg, 'EIniFileException');
  Ini.Free;

  Section('encodings (RTL-681)');
  WriteText(Dir + 'bom.ini', '[S]'#10'K=' + #$00E9, TEncoding.UTF8, True);
  Ini := TMemIniFile.Create(Dir + 'bom.ini');
  CheckEqS(#$00E9, Ini.ReadString('S', 'K', ''), 'UTF-8 BOM detected');
  Ini.WriteString('S', 'K2', 'x');
  Ini.UpdateFile;
  Ini.Free;
  CheckEqS('EFBBBF5B', Hex(Dir + 'bom.ini', 4), 'UTF-8 BOM preserved');
  WriteText(Dir + 'u16.ini', '[S]'#10'K=' + #$20AC, TEncoding.Unicode, True);
  Ini := TMemIniFile.Create(Dir + 'u16.ini');
  CheckEqS(#$20AC, Ini.ReadString('S', 'K', ''), 'UTF-16 BOM detected');
  Ini.WriteString('S', 'K2', 'x');
  Ini.UpdateFile;
  Ini.Free;
  CheckEqS('FFFE5B00', Hex(Dir + 'u16.ini', 4), 'UTF-16 BOM preserved');
  WriteText(Dir + 'plain.ini', '[S]'#10'K=' + #$00E9, TEncoding.UTF8, False);
  Ini := TMemIniFile.Create(Dir + 'plain.ini');
  CheckEqS(#$00E9, Ini.ReadString('S', 'K', ''), 'UTF-8 without BOM by default');
  Ini.Free;
  L.Free;
  Done;
end.
