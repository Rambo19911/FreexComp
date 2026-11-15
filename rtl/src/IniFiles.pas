{ IniFiles - ini files read and rewritten by our own parser, comments and order preserved (docs: rtl/other-units.md
  section 9). }
unit IniFiles;

interface

uses
  SysUtils, Classes;

type
  EIniFileException = class(Exception);

  TCustomIniFile = class(TObject)
  private
    FFileName: string;
  public
    constructor Create(const FileName: string);
    function SectionExists(const Section: string): Boolean;
    function ValueExists(const Section, Ident: string): Boolean;
    function ReadString(const Section, Ident, Default: string): string; virtual; abstract;
    procedure WriteString(const Section, Ident, Value: string); virtual; abstract;
    function ReadInteger(const Section, Ident: string; Default: Integer): Integer; virtual;
    procedure WriteInteger(const Section, Ident: string; Value: Integer); virtual;
    function ReadInt64(const Section, Ident: string; Default: Int64): Int64; virtual;
    procedure WriteInt64(const Section, Ident: string; Value: Int64); virtual;
    function ReadBool(const Section, Ident: string; Default: Boolean): Boolean; virtual;
    procedure WriteBool(const Section, Ident: string; Value: Boolean); virtual;
    function ReadFloat(const Section, Ident: string; Default: Double): Double; virtual;
    procedure WriteFloat(const Section, Ident: string; Value: Double); virtual;
    function ReadDate(const Section, Ident: string; Default: TDateTime): TDateTime; virtual;
    procedure WriteDate(const Section, Ident: string; Value: TDateTime); virtual;
    function ReadTime(const Section, Ident: string; Default: TDateTime): TDateTime; virtual;
    procedure WriteTime(const Section, Ident: string; Value: TDateTime); virtual;
    function ReadDateTime(const Section, Ident: string; Default: TDateTime): TDateTime; virtual;
    procedure WriteDateTime(const Section, Ident: string; Value: TDateTime); virtual;
    procedure ReadSection(const Section: string; Strings: TStrings); virtual; abstract;
    procedure ReadSections(Strings: TStrings); virtual; abstract;
    procedure ReadSectionValues(const Section: string; Strings: TStrings); virtual; abstract;
    procedure EraseSection(const Section: string); virtual; abstract;
    procedure DeleteKey(const Section, Ident: string); virtual; abstract;
    procedure UpdateFile; virtual; abstract;
    property FileName: string read FFileName;
  end;

  { the whole file in memory as its lines; reads parse them, writes edit them in place (RTL-682 ... RTL-684) }
  TMemIniFile = class(TCustomIniFile)
  private
    FLines: TStringList;
    FEncoding: TEncoding;
    FWriteBOM: Boolean;
    FCaseSensitive: Boolean;
    FAutoSave: Boolean;
    FModified: Boolean;
    procedure LoadValues;
    function SameName(const A, B: string): Boolean;
    function HeaderName(Index: Integer; out Name: string): Boolean;
    function KeyName(Index: Integer; out Name: string): Boolean;
    function SectionEnd(Start: Integer): Integer;
    function FindKey(const Section, Ident: string): Integer;
    function FirstHeader(const Section: string): Integer;
  public
    constructor Create(const FileName: string); overload;
    constructor Create(const FileName: string; Encoding: TEncoding); overload;
    destructor Destroy; override;
    procedure Clear;
    procedure GetStrings(List: TStrings);
    procedure SetStrings(List: TStrings);
    procedure Rename(const FileName: string; Reload: Boolean);
    function ReadString(const Section, Ident, Default: string): string; override;
    procedure WriteString(const Section, Ident, Value: string); override;
    procedure ReadSection(const Section: string; Strings: TStrings); override;
    procedure ReadSections(Strings: TStrings); override;
    procedure ReadSectionValues(const Section: string; Strings: TStrings); override;
    procedure EraseSection(const Section: string); override;
    procedure DeleteKey(const Section, Ident: string); override;
    procedure UpdateFile; override;
    property CaseSensitive: Boolean read FCaseSensitive write FCaseSensitive;
    property AutoSave: Boolean read FAutoSave write FAutoSave;
    property Encoding: TEncoding read FEncoding write FEncoding;
  end;

  { the same implementation; every change is written to the file at once }
  TIniFile = class(TMemIniFile)
  public
    constructor Create(const FileName: string); overload;
    constructor Create(const FileName: string; Encoding: TEncoding); overload;
    procedure WriteString(const Section, Ident, Value: string); override;
    procedure EraseSection(const Section: string); override;
    procedure DeleteKey(const Section, Ident: string); override;
  end;

implementation

uses
  Windows, DateUtils, RTLConsts;

{ ---------------------------------------------------------------- TCustomIniFile }

constructor TCustomIniFile.Create(const FileName: string);
begin
  inherited Create;
  FFileName := FileName;
end;

function TCustomIniFile.SectionExists(const Section: string): Boolean;
var
  L: TStringList;
begin
  L := TStringList.Create;
  try
    ReadSections(L);
    Result := L.IndexOf(Section) >= 0;
  finally
    L.Free;
  end;
end;

function TCustomIniFile.ValueExists(const Section, Ident: string): Boolean;
var
  L: TStringList;
begin
  L := TStringList.Create;
  try
    ReadSection(Section, L);
    Result := L.IndexOf(Ident) >= 0;
  finally
    L.Free;
  end;
end;

{ StrToIntDef: decimal or $hex }
function TCustomIniFile.ReadInteger(const Section, Ident: string; Default: Integer): Integer;
begin
  Result := StrToIntDef(ReadString(Section, Ident, ''), Default);
end;

procedure TCustomIniFile.WriteInteger(const Section, Ident: string; Value: Integer);
begin
  WriteString(Section, Ident, IntToStr(Value));
end;

function TCustomIniFile.ReadInt64(const Section, Ident: string; Default: Int64): Int64;
begin
  Result := StrToInt64Def(ReadString(Section, Ident, ''), Default);
end;

procedure TCustomIniFile.WriteInt64(const Section, Ident: string; Value: Int64);
begin
  WriteString(Section, Ident, IntToStr(Value));
end;

{ stored as 1 / 0; any integer other than 0 reads as True }
function TCustomIniFile.ReadBool(const Section, Ident: string; Default: Boolean): Boolean;
begin
  Result := ReadInteger(Section, Ident, Ord(Default)) <> 0;
end;

procedure TCustomIniFile.WriteBool(const Section, Ident: string; Value: Boolean);
begin
  if Value then
    WriteString(Section, Ident, '1')
  else
    WriteString(Section, Ident, '0');
end;

{ invariant text, independent of FormatSettings }
function TCustomIniFile.ReadFloat(const Section, Ident: string; Default: Double): Double;
begin
  if not TryStrToFloat(ReadString(Section, Ident, ''), Result, InvariantFormatSettings) then
    Result := Default;
end;

procedure TCustomIniFile.WriteFloat(const Section, Ident: string; Value: Double);
begin
  WriteString(Section, Ident, FloatToStr(Value, InvariantFormatSettings));
end;

function Pad(Value, Digits: Integer): string;
begin
  Result := IntToStr(Value);
  while Length(Result) < Digits do
    Result := '0' + Result;
end;

{ ISO 8601: yyyy-mm-dd, hh:nn:ss.zzz }
function DateText(Value: TDateTime): string;
var
  Y, M, D: Word;
begin
  DecodeDate(Value, Y, M, D);
  Result := Pad(Y, 4) + '-' + Pad(M, 2) + '-' + Pad(D, 2);
end;

function TimeText(Value: TDateTime): string;
var
  H, N, S, Z: Word;
begin
  DecodeTime(Value, H, N, S, Z);
  Result := Pad(H, 2) + ':' + Pad(N, 2) + ':' + Pad(S, 2) + '.' + Pad(Z, 3);
end;

function TCustomIniFile.ReadDate(const Section, Ident: string; Default: TDateTime): TDateTime;
var
  S: string;
begin
  S := ReadString(Section, Ident, '');
  if not TryISO8601ToDate(S, Result, True) then
    Result := Default;
end;

procedure TCustomIniFile.WriteDate(const Section, Ident: string; Value: TDateTime);
begin
  WriteString(Section, Ident, DateText(Value));
end;

{ a time alone is read as the time of day 0 (1899-12-30) }
function TCustomIniFile.ReadTime(const Section, Ident: string; Default: TDateTime): TDateTime;
var
  S: string;
begin
  S := ReadString(Section, Ident, '');
  if (S = '') or not TryISO8601ToDate('1899-12-30T' + S, Result, True) then
    Result := Default;
end;

procedure TCustomIniFile.WriteTime(const Section, Ident: string; Value: TDateTime);
begin
  WriteString(Section, Ident, TimeText(Value));
end;

function TCustomIniFile.ReadDateTime(const Section, Ident: string; Default: TDateTime): TDateTime;
var
  S: string;
begin
  S := ReadString(Section, Ident, '');
  if not TryISO8601ToDate(S, Result, True) then
    Result := Default;
end;

procedure TCustomIniFile.WriteDateTime(const Section, Ident: string; Value: TDateTime);
begin
  WriteString(Section, Ident, DateText(Value) + 'T' + TimeText(Value));
end;

{ ---------------------------------------------------------------- TMemIniFile }

constructor TMemIniFile.Create(const FileName: string);
begin
  Create(FileName, nil);
end;

constructor TMemIniFile.Create(const FileName: string; Encoding: TEncoding);
begin
  inherited Create(FileName);
  FLines := TStringList.Create;
  FEncoding := Encoding;
  LoadValues;
end;

destructor TMemIniFile.Destroy;
begin
  if FAutoSave and FModified then
    UpdateFile;
  FLines.Free;
  inherited Destroy;
end;

{ RTL-680, RTL-681: a missing file is empty; a BOM decides the encoding (else the given one, else UTF-8) and is
  written back }
procedure TMemIniFile.LoadValues;
var
  S: TFileStream;
  Bytes: TBytes;
  Enc: TEncoding;
  Skip: Integer;
begin
  FLines.Clear;
  FModified := False;
  FWriteBOM := False;
  if (FileName = '') or not FileExists(FileName) then
    Exit;
  S := TFileStream.Create(FileName, fmOpenRead or fmShareDenyWrite);
  try
    SetLength(Bytes, Integer(S.Size));
    if Length(Bytes) > 0 then
      S.ReadBuffer(Bytes[0], Length(Bytes));
  finally
    S.Free;
  end;
  Enc := nil;
  Skip := TEncoding.GetBufferEncoding(Bytes, Enc, nil);
  if Skip > 0 then
  begin
    FEncoding := Enc;
    FWriteBOM := True;
  end
  else if FEncoding = nil then
    FEncoding := TEncoding.UTF8
  else
    Skip := TEncoding.GetBufferEncoding(Bytes, FEncoding, nil);
  FLines.Text := FEncoding.GetString(Bytes, Skip, Length(Bytes) - Skip);
end;

{ RTL-683 }
function TMemIniFile.SameName(const A, B: string): Boolean;
begin
  if FCaseSensitive then
    Result := A = B
  else
    Result := CompareText(A, B) = 0;
end;

{ RTL-682: "[Section]": the text between the first "[" and the last "]", trimmed }
function TMemIniFile.HeaderName(Index: Integer; out Name: string): Boolean;
var
  S: string;
  P: Integer;
begin
  S := Trim(FLines[Index]);
  Result := False;
  Name := '';
  if (S = '') or (S[1] <> '[') then
    Exit;
  P := Length(S);
  while (P > 1) and (S[P] <> ']') do
    P := P - 1;
  if P <= 1 then
    Exit;
  Name := Trim(Copy(S, 2, P - 2));
  Result := True;
end;

{ "Ident=Value": the identifier before the first "=", trimmed; comments and lines without "=" are no keys }
function TMemIniFile.KeyName(Index: Integer; out Name: string): Boolean;
var
  S: string;
  P: Integer;
begin
  S := Trim(FLines[Index]);
  Name := '';
  Result := False;
  if (S = '') or (S[1] = ';') or (S[1] = '#') or (S[1] = '[') then
    Exit;
  P := Pos('=', S);
  if P = 0 then
    Exit;
  Name := Trim(Copy(S, 1, P - 1));
  Result := True;
end;

{ the index after the last line of the section whose header (or -1 for section '') is at Start }
function TMemIniFile.SectionEnd(Start: Integer): Integer;
var
  Name: string;
begin
  Result := Start + 1;
  while (Result < FLines.Count) and not HeaderName(Result, Name) do
    Result := Result + 1;
end;

function TMemIniFile.FirstHeader(const Section: string): Integer;
var
  Name: string;
begin
  for Result := 0 to FLines.Count - 1 do
    if HeaderName(Result, Name) and SameName(Name, Section) then
      Exit;
  Result := -1;
end;

{ the line of the first key Ident in any occurrence of Section, or -1 }
function TMemIniFile.FindKey(const Section, Ident: string): Integer;
var
  I: Integer;
  Name: string;
  InSection: Boolean;
begin
  InSection := Section = '';
  for I := 0 to FLines.Count - 1 do
    if HeaderName(I, Name) then
      InSection := (Section <> '') and SameName(Name, Section)
    else if InSection and KeyName(I, Name) and SameName(Name, Ident) then
    begin
      Result := I;
      Exit;
    end;
  Result := -1;
end;

function TMemIniFile.ReadString(const Section, Ident, Default: string): string;
var
  I, P: Integer;
  S: string;
begin
  I := FindKey(Section, Ident);
  if I < 0 then
  begin
    Result := Default;
    Exit;
  end;
  S := Trim(FLines[I]);
  P := Pos('=', S);
  Result := Trim(Copy(S, P + 1, MaxInt));
  if (Length(Result) >= 2) and ((Result[1] = '"') or (Result[1] = '''')) and (Result[Length(Result)] = Result[1]) then
    Result := Copy(Result, 2, Length(Result) - 2);
end;

{ RTL-683, RTL-684: the first existing key is updated (keeping its identifier); a new key goes after the last key of
  the section's first occurrence (else right after its header); a new section is appended after one empty line }
procedure TMemIniFile.WriteString(const Section, Ident, Value: string);
var
  I, Start, Stop, At: Integer;
  Name: string;
begin
  FModified := True;
  I := FindKey(Section, Ident);
  if I >= 0 then
  begin
    KeyName(I, Name);
    FLines[I] := Name + '=' + Value;
    Exit;
  end;
  if Section = '' then
    Start := -1
  else
    Start := FirstHeader(Section);
  if (Start < 0) and (Section <> '') then
  begin
    if FLines.Count > 0 then
      FLines.Add('');
    FLines.Add('[' + Section + ']');
    FLines.Add(Ident + '=' + Value);
    Exit;
  end;
  Stop := SectionEnd(Start);
  At := Start + 1;
  for I := Start + 1 to Stop - 1 do
    if KeyName(I, Name) then
      At := I + 1;
  FLines.Insert(At, Ident + '=' + Value);
end;

{ identifiers of every occurrence of Section, each once, in file order }
procedure TMemIniFile.ReadSection(const Section: string; Strings: TStrings);
var
  I, J: Integer;
  Name, Current: string;
  InSection, Seen: Boolean;
begin
  Strings.BeginUpdate;
  try
    Strings.Clear;
    InSection := Section = '';
    for I := 0 to FLines.Count - 1 do
      if HeaderName(I, Current) then
        InSection := (Section <> '') and SameName(Current, Section)
      else if InSection and KeyName(I, Name) then
      begin
        Seen := False;
        for J := 0 to Strings.Count - 1 do
          if SameName(Strings[J], Name) then
            Seen := True;
        if not Seen then
          Strings.Add(Name);
      end;
  finally
    Strings.EndUpdate;
  end;
end;

{ section names in the order of their first occurrence; section '' is not listed }
procedure TMemIniFile.ReadSections(Strings: TStrings);
var
  I, J: Integer;
  Name: string;
  Seen: Boolean;
begin
  Strings.BeginUpdate;
  try
    Strings.Clear;
    for I := 0 to FLines.Count - 1 do
      if HeaderName(I, Name) then
      begin
        Seen := False;
        for J := 0 to Strings.Count - 1 do
          if SameName(Strings[J], Name) then
            Seen := True;
        if not Seen then
          Strings.Add(Name);
      end;
  finally
    Strings.EndUpdate;
  end;
end;

procedure TMemIniFile.ReadSectionValues(const Section: string; Strings: TStrings);
var
  Idents: TStringList;
  I: Integer;
begin
  Idents := TStringList.Create;
  try
    ReadSection(Section, Idents);
    Strings.BeginUpdate;
    try
      Strings.Clear;
      for I := 0 to Idents.Count - 1 do
        Strings.Add(Idents[I] + '=' + ReadString(Section, Idents[I], ''));
    finally
      Strings.EndUpdate;
    end;
  finally
    Idents.Free;
  end;
end;

{ every occurrence: its header and all its lines }
procedure TMemIniFile.EraseSection(const Section: string);
var
  Start, Stop, I: Integer;
begin
  FModified := True;
  if Section = '' then
  begin
    Stop := SectionEnd(-1);
    for I := Stop - 1 downto 0 do
      FLines.Delete(I);
    Exit;
  end;
  Start := FirstHeader(Section);
  while Start >= 0 do
  begin
    Stop := SectionEnd(Start);
    for I := Stop - 1 downto Start do
      FLines.Delete(I);
    Start := FirstHeader(Section);
  end;
end;

{ every line of the key in the section }
procedure TMemIniFile.DeleteKey(const Section, Ident: string);
var
  I: Integer;
begin
  FModified := True;
  I := FindKey(Section, Ident);
  while I >= 0 do
  begin
    FLines.Delete(I);
    I := FindKey(Section, Ident);
  end;
end;

{ RTL-680: the whole file, CRLF, through <name>.tmp and MoveFileExW }
procedure TMemIniFile.UpdateFile;
var
  Tmp: string;
  S: TFileStream;
  Bytes: TBytes;
  Enc: TEncoding;
  Ok: Boolean;
begin
  Enc := FEncoding;
  if Enc = nil then
    Enc := TEncoding.UTF8;
  Tmp := FileName + '.tmp';
  Ok := False;
  try
    S := TFileStream.Create(Tmp, fmCreate);
    try
      if FWriteBOM then
      begin
        Bytes := Enc.GetPreamble;
        if Length(Bytes) > 0 then
          S.WriteBuffer(Bytes[0], Length(Bytes));
      end;
      FLines.LineBreak := #13#10;
      Bytes := Enc.GetBytes(FLines.Text);
      if Length(Bytes) > 0 then
        S.WriteBuffer(Bytes[0], Length(Bytes));
    finally
      S.Free;
    end;
    Ok := MoveFileExW(PChar(Tmp), PChar(FileName), MOVEFILE_REPLACE_EXISTING);
  except
    on E: EStreamError do
      Ok := False;
  end;
  if not Ok then
  begin
    DeleteFileW(PChar(Tmp));
    raise EIniFileException.CreateFmt(SIniFileWriteError, [FileName]);
  end;
  FModified := False;
end;

procedure TMemIniFile.Clear;
begin
  FLines.Clear;
  FModified := True;
end;

procedure TMemIniFile.GetStrings(List: TStrings);
begin
  List.Assign(FLines);
end;

procedure TMemIniFile.SetStrings(List: TStrings);
begin
  FLines.Assign(List);
  FModified := True;
end;

procedure TMemIniFile.Rename(const FileName: string; Reload: Boolean);
begin
  FFileName := FileName;
  if Reload then
    LoadValues;
end;

{ ---------------------------------------------------------------- TIniFile }

constructor TIniFile.Create(const FileName: string);
begin
  Create(FileName, nil);
end;

constructor TIniFile.Create(const FileName: string; Encoding: TEncoding);
begin
  inherited Create(FileName, Encoding);
  AutoSave := True;
end;

procedure TIniFile.WriteString(const Section, Ident, Value: string);
begin
  inherited WriteString(Section, Ident, Value);
  UpdateFile;
end;

procedure TIniFile.EraseSection(const Section: string);
begin
  inherited EraseSection(Section);
  UpdateFile;
end;

procedure TIniFile.DeleteKey(const Section, Ident: string);
begin
  inherited DeleteKey(Section, Ident);
  UpdateFile;
end;

end.
