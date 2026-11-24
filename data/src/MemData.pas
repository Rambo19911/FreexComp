{ MemData - TMemTable, the dataset whose rows live only in memory: defined by field definitions (or by its
  persistent data fields), rows kept across Close, auto-increment counters, and a JSON form for saving and
  loading. The JSON writer and reader are private to this unit.
  Must not use any UI-framework unit. }
unit MemData;

interface

uses
  SysUtils, Classes, DB;

resourcestring
  SMemNoFieldDefs = 'Cannot open a table without field definitions';
  SMemInvalidData = 'Invalid table data: %s at offset %d';
  SMemNonFinite = 'Cannot save non-finite value of field ''%s''';

type
  TMemTable = class(TRowDataSet)
  private
    FReadOnly: Boolean;
    { the rows of the closed table; while the table is open they are in Rows and this list is empty }
    FStore: TDataRowList;
    FLive: Boolean;
    { the next value of every auto-increment column, indexed like FieldDefs; empty = all 1 }
    FNextValues: array of Int64;
    { set while the table itself replaces FieldDefs: the rows are kept }
    FKeepRows: Boolean;
    { a load in progress: the text DriverOpen parses, the definitions it found }
    FLoading: Boolean;
    FLoadText: string;
    FLoadDefs: TFieldDefs;
    procedure StoreRows;
    procedure DiscardRows;
    procedure NeedCounters;
    procedure ReplaceDefs(Source: TFieldDefs);
    function DataFieldCount: Integer;
    procedure LoadBytes(const Bytes: TBytes);
  protected
    procedure DefsChanged; override;
    procedure InternalClose; override;
    procedure InternalRefresh; override;
    procedure DriverOpen; override;
    procedure DriverInitFieldDefs; override;
    procedure DriverLoadRows; override;
    procedure DriverClose; override;
    function DriverCanModify: Boolean; override;
    procedure ApplyInsert(Buffer: TDataRow); override;
    procedure ApplyUpdate(OldRow, Buffer: TDataRow); override;
    procedure ApplyDelete(Row: TDataRow); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { Opens an empty table from FieldDefs; an open table is closed first and loses its rows. }
    procedure CreateDataSet;
    { Removes all rows; the table stays open. A pending edit is cancelled first. }
    procedure EmptyDataSet;
    { Takes over the data fields of Source (an auto-increment field as an integer field) and the rows of its
      view; the cursor of Source does not move. }
    procedure CopyDataSet(Source: TDataSet);
    { The table as JSON text (UTF-8 without a byte order mark in a file or stream): definitions,
      auto-increment counters and all rows; a pending edit is posted first. }
    procedure SaveToFile(const FileName: string);
    procedure SaveToStream(Stream: TStream);
    function SaveToJSON: string;
    { Replaces definitions and rows by those of the text. A bad text raises EDatabaseError and leaves the
      table closed and empty. }
    procedure LoadFromFile(const FileName: string);
    procedure LoadFromStream(Stream: TStream);
    procedure LoadFromJSON(const JSON: string);
  published
    property Active;
    { stored in a form file as a collection }
    property FieldDefs;
    property ReadOnly: Boolean read FReadOnly write FReadOnly default False;
    property Filter;
    property Filtered;
    property FilterOptions;
    property BeforeOpen;
    property AfterOpen;
    property BeforeClose;
    property AfterClose;
    property BeforeScroll;
    property AfterScroll;
    property BeforeInsert;
    property AfterInsert;
    property BeforeEdit;
    property AfterEdit;
    property BeforePost;
    property AfterPost;
    property BeforeCancel;
    property AfterCancel;
    property BeforeDelete;
    property AfterDelete;
    property BeforeRefresh;
    property AfterRefresh;
    property OnNewRecord;
    property OnPostError;
    property OnDeleteError;
    property OnEditError;
    property OnCalcFields;
    property OnFilterRecord;
  end;

implementation

const
  LF = #10;
  MSecsInDay = 86400000;
  { the names of the field types in the JSON form }
  TypeNames: array[TFieldType] of string = ('Unknown', 'String', 'Smallint', 'Integer', 'Word', 'Boolean', 'Float',
    'Currency', 'Date', 'Time', 'DateTime', 'AutoInc', 'Blob', 'Memo', 'Largeint');
  Base64Chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
  HexChars = '0123456789ABCDEF';
  MaxDepth = 100;

function CopyRow(Source: TDataRow): TDataRow;
var
  I: Integer;
begin
  Result := TDataRow.Create(Length(Source.Values));
  for I := 0 to High(Source.Values) do
    Result.Values[I] := Source.Values[I];
end;

{ ---- JSON writer ---- }

function Digits(Value, Width: Integer): string;
begin
  Result := IntToStr(Value);
  while Length(Result) < Width do
    Result := '0' + Result;
end;

{ A JSON string: the short escapes, \u00XX for the other characters below U+0020, \uDXXX for a surrogate
  without its partner, everything else as it is. }
procedure AppendString(B: TStringBuilder; const S: string);
var
  I, Code: Integer;
  C: Char;
  Escape: Boolean;
begin
  B.Append('"');
  I := 1;
  while I <= Length(S) do
  begin
    C := S[I];
    Code := Ord(C);
    Escape := False;
    case Code of
      $22: B.Append('\"');
      $5C: B.Append('\\');
      $0A: B.Append('\n');
      $0D: B.Append('\r');
      $09: B.Append('\t');
      $08: B.Append('\b');
      $0C: B.Append('\f');
    else
      if Code < $20 then
        Escape := True
      else if (Code >= $D800) and (Code <= $DBFF) then
      begin
        if (I < Length(S)) and (Ord(S[I + 1]) >= $DC00) and (Ord(S[I + 1]) <= $DFFF) then
        begin
          B.Append(C);
          I := I + 1;
          B.Append(S[I]);
        end
        else
          Escape := True;
      end
      else if (Code >= $DC00) and (Code <= $DFFF) then
        Escape := True
      else
        B.Append(C);
    end;
    if Escape then
    begin
      B.Append('\u');
      B.Append(HexChars[(Code shr 12) and 15 + 1]);
      B.Append(HexChars[(Code shr 8) and 15 + 1]);
      B.Append(HexChars[(Code shr 4) and 15 + 1]);
      B.Append(HexChars[Code and 15 + 1]);
    end;
    I := I + 1;
  end;
  B.Append('"');
end;

{ The shortest decimal text that reads back to the same Double: plain digits for a decimal exponent of -7
  to 20, else the form 1.5e-7 / 1e+30. False for a NaN or an infinity. }
function FloatText(Value: Double; out Text: string): Boolean;
var
  R: TFloatDigits;
  I, N: Integer;
  S: string;
begin
  _FloatDigitsShortest(Value, False, R);
  Result := not (R.IsNaN or R.IsInfinite);
  if not Result then
    Exit;
  if R.Count = 0 then
    S := '0'
  else
  begin
    S := '';
    N := R.Exponent;                       { the value is 0.d1d2... x 10^N }
    if (N >= R.Count) and (N <= 21) then
    begin
      for I := 0 to R.Count - 1 do
        S := S + Chr(Ord('0') + R.Digits[I]);
      for I := R.Count + 1 to N do
        S := S + '0';
    end
    else if (N > 0) and (N <= 21) then
    begin
      for I := 0 to R.Count - 1 do
      begin
        if I = N then
          S := S + '.';
        S := S + Chr(Ord('0') + R.Digits[I]);
      end;
    end
    else if (N > -6) and (N <= 0) then
    begin
      S := '0.';
      for I := 1 to -N do
        S := S + '0';
      for I := 0 to R.Count - 1 do
        S := S + Chr(Ord('0') + R.Digits[I]);
    end
    else
    begin
      S := Chr(Ord('0') + R.Digits[0]);
      if R.Count > 1 then
      begin
        S := S + '.';
        for I := 1 to R.Count - 1 do
          S := S + Chr(Ord('0') + R.Digits[I]);
      end;
      if N - 1 < 0 then
        S := S + 'e-' + IntToStr(1 - N)
      else
        S := S + 'e+' + IntToStr(N - 1);
    end;
  end;
  if R.Negative then
    S := '-' + S;
  Text := S;
end;

{ The day number and the milliseconds of the day of a date-time value; the time of day is the absolute
  fraction, also for the days before day 0. }
procedure SplitDateTime(Value: Double; out Day: Double; out MSecs: Integer);
var
  M: Int64;
begin
  Day := Int(Value);
  M := Round(Abs(Value - Day) * MSecsInDay);
  if M >= MSecsInDay then
  begin
    M := M - MSecsInDay;
    Day := Day + 1;
  end;
  MSecs := Integer(M);
end;

function DateText(Day: Double): string;
var
  Y, M, D: Word;
begin
  DecodeDate(Day, Y, M, D);
  Result := Digits(Y, 4) + '-' + Digits(M, 2) + '-' + Digits(D, 2);
end;

function TimeText(MSecs: Integer): string;
begin
  Result := Digits(MSecs div 3600000, 2) + ':' + Digits((MSecs div 60000) mod 60, 2) + ':' +
    Digits((MSecs div 1000) mod 60, 2);
  if MSecs mod 1000 <> 0 then
    Result := Result + '.' + Digits(MSecs mod 1000, 3);
end;

function Base64Text(const Bytes: TBytes): string;
var
  I, N, Triple: Integer;
begin
  Result := '';
  N := Length(Bytes);
  I := 0;
  while I < N do
  begin
    Triple := Bytes[I] shl 16;
    if I + 1 < N then
      Triple := Triple or (Bytes[I + 1] shl 8);
    if I + 2 < N then
      Triple := Triple or Bytes[I + 2];
    Result := Result + Base64Chars[(Triple shr 18) and 63 + 1] + Base64Chars[(Triple shr 12) and 63 + 1];
    if I + 1 < N then
      Result := Result + Base64Chars[(Triple shr 6) and 63 + 1]
    else
      Result := Result + '=';
    if I + 2 < N then
      Result := Result + Base64Chars[Triple and 63 + 1]
    else
      Result := Result + '=';
    I := I + 3;
  end;
end;

{ One stored value in the form of its field type; a value that is not of the canonical kind of the type
  is written as null. }
procedure AppendValue(B: TStringBuilder; Def: TFieldDef; const V: TDBValue);
var
  Text: string;
  Day: Double;
  MSecs: Integer;
begin
  if (V.Kind = vkNull) or (V.Kind <> CanonicalKind(Def.DataType)) then
  begin
    B.Append('null');
    Exit;
  end;
  case Def.DataType of
    ftSmallint, ftInteger, ftWord, ftAutoInc, ftLargeint:
      B.Append(IntToStr(V.IntVal));
    ftBoolean:
      if V.IntVal <> 0 then
        B.Append('true')
      else
        B.Append('false');
    ftFloat, ftCurrency:
      begin
        if not FloatText(V.FloatVal, Text) then
          DatabaseErrorFmt(SMemNonFinite, [Def.Name]);
        B.Append(Text);
      end;
    ftString, ftMemo:
      AppendString(B, V.StrVal);
    ftDate:
      B.Append('"' + DateText(Int(V.FloatVal)) + '"');
    ftTime:
      begin
        SplitDateTime(V.FloatVal, Day, MSecs);
        B.Append('"' + TimeText(MSecs) + '"');
      end;
    ftDateTime:
      begin
        SplitDateTime(V.FloatVal, Day, MSecs);
        B.Append('"' + DateText(Day) + 'T' + TimeText(MSecs) + '"');
      end;
    ftBlob:
      B.Append('"' + Base64Text(V.BytesVal) + '"');
  else
    B.Append('null');
  end;
end;

{ ---- JSON reader ---- }

type
  { Reads a table text. Offsets in errors are 0-based character offsets: for a syntax error the first
    character that cannot continue a JSON text (the length of the text at a premature end). }
  TTableReader = class
  private
    FText: string;
    FPos: Integer;                        { 1-based index of the next character }
    procedure Fail(const Reason: string; Offset: Integer);
    procedure SyntaxError;
    function AtEnd: Boolean;
    function Current: Char;
    procedure SkipSpace;
    procedure Expect(C: Char);
    procedure ReadLiteral(const Word: string);
    function ReadString: string;
    { a number token; IsInteger: no fraction and no exponent }
    function ReadNumber(out IsInteger: Boolean): string;
    procedure SkipValue(Depth: Integer);
    { a member name and its colon; the position is at the member value }
    function ReadName: string;
    { after an array element or object member: True when another follows, False at the closing character }
    function More(Closing: Char): Boolean;
    { after the opening character: True when the array or object is empty (and passed) }
    function EmptyGroup(Closing: Char): Boolean;
    procedure ReadField(Defs: TFieldDefs);
    function ReadValue(Def: TFieldDef): TDBValue;
    function ReadDateTime(DataType: TFieldType; const S: string; out Value: Double): Boolean;
    function ReadBase64(const S: string; out Bytes: TBytes): Boolean;
  public
    { after ReadTable: the auto-increment counter of every definition }
    NextValues: array of Int64;
    constructor Create(const AText: string);
    { Fills Defs, Rows (one row per element of "rows") and NextValues. }
    procedure ReadTable(Defs: TFieldDefs; Rows: TDataRowList);
  end;

constructor TTableReader.Create(const AText: string);
begin
  inherited Create;
  FText := AText;
  FPos := 1;
end;

procedure TTableReader.Fail(const Reason: string; Offset: Integer);
begin
  DatabaseErrorFmt(SMemInvalidData, [Reason, Offset]);
end;

procedure TTableReader.SyntaxError;
begin
  Fail('JSON syntax error', FPos - 1);
end;

function TTableReader.AtEnd: Boolean;
begin
  Result := FPos > Length(FText);
end;

{ the next character; a space at the end of the text (never a valid token start) }
function TTableReader.Current: Char;
begin
  if FPos > Length(FText) then
    Result := ' '
  else
    Result := FText[FPos];
end;

procedure TTableReader.SkipSpace;
var
  C: Char;
begin
  while FPos <= Length(FText) do
  begin
    C := FText[FPos];
    if (C <> ' ') and (C <> #9) and (C <> #10) and (C <> #13) then
      Exit;
    FPos := FPos + 1;
  end;
end;

procedure TTableReader.Expect(C: Char);
begin
  if AtEnd or (FText[FPos] <> C) then
    SyntaxError;
  FPos := FPos + 1;
end;

procedure TTableReader.ReadLiteral(const Word: string);
var
  I: Integer;
begin
  for I := 1 to Length(Word) do
    Expect(Word[I]);
end;

function TTableReader.ReadString: string;
var
  B: TStringBuilder;
  C: Char;
  I, Code, Digit: Integer;
begin
  Expect('"');
  B := TStringBuilder.Create;
  try
    while True do
    begin
      if AtEnd then
        SyntaxError;
      C := FText[FPos];
      if C = '"' then
      begin
        FPos := FPos + 1;
        Break;
      end;
      if Ord(C) < $20 then
        SyntaxError;
      if C <> '\' then
      begin
        B.Append(C);
        FPos := FPos + 1;
        Continue;
      end;
      FPos := FPos + 1;
      if AtEnd then
        SyntaxError;
      C := FText[FPos];
      case C of
        '"', '\', '/': B.Append(C);
        'b': B.Append(#8);
        'f': B.Append(#12);
        'n': B.Append(#10);
        'r': B.Append(#13);
        't': B.Append(#9);
        'u':
          begin
            Code := 0;
            for I := 1 to 4 do
            begin
              FPos := FPos + 1;
              if AtEnd then
                SyntaxError;
              C := FText[FPos];
              if (C >= '0') and (C <= '9') then
                Digit := Ord(C) - Ord('0')
              else if (C >= 'a') and (C <= 'f') then
                Digit := Ord(C) - Ord('a') + 10
              else if (C >= 'A') and (C <= 'F') then
                Digit := Ord(C) - Ord('A') + 10
              else
              begin
                Digit := 0;
                SyntaxError;
              end;
              Code := Code * 16 + Digit;
            end;
            B.Append(Chr(Code));
          end;
      else
        SyntaxError;
      end;
      FPos := FPos + 1;
    end;
    Result := B.ToString;
  finally
    B.Free;
  end;
end;

function TTableReader.ReadNumber(out IsInteger: Boolean): string;
var
  Start: Integer;

  procedure DigitRun;
  begin
    if (Current < '0') or (Current > '9') then
      SyntaxError;
    while (Current >= '0') and (Current <= '9') do
      FPos := FPos + 1;
  end;

begin
  Start := FPos;
  IsInteger := True;
  if Current = '-' then
    FPos := FPos + 1;
  if Current = '0' then
    FPos := FPos + 1
  else
    DigitRun;
  if Current = '.' then
  begin
    IsInteger := False;
    FPos := FPos + 1;
    DigitRun;
  end;
  if (Current = 'e') or (Current = 'E') then
  begin
    IsInteger := False;
    FPos := FPos + 1;
    if (Current = '+') or (Current = '-') then
      FPos := FPos + 1;
    DigitRun;
  end;
  Result := Copy(FText, Start, FPos - Start);
end;

function TTableReader.EmptyGroup(Closing: Char): Boolean;
begin
  SkipSpace;
  Result := not AtEnd and (FText[FPos] = Closing);
  if Result then
    FPos := FPos + 1;
end;

function TTableReader.More(Closing: Char): Boolean;
begin
  SkipSpace;
  if not AtEnd and (FText[FPos] = Closing) then
  begin
    FPos := FPos + 1;
    Result := False;
  end
  else
  begin
    Expect(',');
    SkipSpace;
    Result := True;
  end;
end;

function TTableReader.ReadName: string;
begin
  SkipSpace;
  Result := ReadString;
  SkipSpace;
  Expect(':');
  SkipSpace;
end;

{ Passes one value of any kind, checking its syntax. }
procedure TTableReader.SkipValue(Depth: Integer);
var
  IsInteger: Boolean;
begin
  SkipSpace;
  if AtEnd or (Depth > MaxDepth) then
    SyntaxError;
  case FText[FPos] of
    '{':
      begin
        FPos := FPos + 1;
        if not EmptyGroup('}') then
          repeat
            ReadName;
            SkipValue(Depth + 1);
          until not More('}');
      end;
    '[':
      begin
        FPos := FPos + 1;
        if not EmptyGroup(']') then
          repeat
            SkipValue(Depth + 1);
          until not More(']');
      end;
    '"': ReadString;
    't': ReadLiteral('true');
    'f': ReadLiteral('false');
    'n': ReadLiteral('null');
    '-', '0'..'9': ReadNumber(IsInteger);
  else
    SyntaxError;
  end;
end;

{ One element of "fields" (the syntax of the text is known to be good). }
procedure TTableReader.ReadField(Defs: TFieldDefs);
var
  Start, NamePos, TypePos, ValuePos: Integer;
  Key, Name, TypeText, Number: string;
  HasName, TypeIsString, Required, IsInteger: Boolean;
  Size: Int64;
  DataType, T: TFieldType;
begin
  SkipSpace;
  Start := FPos;
  if Current <> '{' then
    SyntaxError;
  FPos := FPos + 1;
  HasName := False;
  TypeIsString := False;
  Required := False;
  Size := 0;
  NamePos := Start;
  TypePos := Start;
  TypeText := '';
  if not EmptyGroup('}') then
    repeat
      Key := ReadName;
      ValuePos := FPos;
      if Key = 'name' then
      begin
        if Current <> '"' then
          SyntaxError;
        Name := ReadString;
        NamePos := ValuePos;
        HasName := True;
      end
      else if Key = 'type' then
      begin
        { a value that is not a string is no type name; the error shows its text }
        TypeIsString := Current = '"';
        if TypeIsString then
          TypeText := ReadString
        else
        begin
          SkipValue(0);
          TypeText := Copy(FText, ValuePos, FPos - ValuePos);
        end;
        TypePos := ValuePos;
      end
      else if Key = 'size' then
      begin
        if (Current <> '-') and ((Current < '0') or (Current > '9')) then
          SyntaxError;
        Number := ReadNumber(IsInteger);
        if not IsInteger or not TryStrToInt64(Number, Size) or (Size < 0) or (Size > High(Integer)) then
        begin
          FPos := ValuePos;
          SyntaxError;
        end;
      end
      else if Key = 'required' then
      begin
        if Current = 't' then
        begin
          ReadLiteral('true');
          Required := True;
        end
        else if Current = 'f' then
        begin
          ReadLiteral('false');
          Required := False;
        end
        else
          SyntaxError;
      end
      else
        SkipValue(0);
    until not More('}');
  if not HasName then
    Fail('JSON syntax error', Start - 1);
  DataType := ftUnknown;
  if TypeIsString then
    for T := Low(TFieldType) to High(TFieldType) do
      if (T <> ftUnknown) and SameText(TypeNames[T], TypeText) then
        DataType := T;
  if DataType = ftUnknown then
    Fail('unknown field type "' + TypeText + '"', TypePos - 1);
  if Defs.IndexOf(Name) >= 0 then
    Fail('duplicate field "' + Name + '"', NamePos - 1);
  Defs.Add(Name, DataType, Integer(Size), Required);
end;

{ 'YYYY-MM-DD', 'HH:MM:SS[.SSS]' or both joined by 'T', as the type asks. }
function TTableReader.ReadDateTime(DataType: TFieldType; const S: string; out Value: Double): Boolean;
var
  Day, Time: TDateTime;
  TimeStart, TimeLength: Integer;

  function Number(Start, Count: Integer; out N: Word): Boolean;
  var
    I: Integer;
  begin
    N := 0;
    Result := True;
    for I := Start to Start + Count - 1 do
      if (S[I] >= '0') and (S[I] <= '9') then
        N := N * 10 + (Ord(S[I]) - Ord('0'))
      else
        Result := False;
  end;

  function DatePart: Boolean;
  var
    Y, M, D: Word;
  begin
    Result := (Length(S) >= 10) and (S[5] = '-') and (S[8] = '-') and Number(1, 4, Y) and Number(6, 2, M) and
      Number(9, 2, D) and TryEncodeDate(Y, M, D, Day);
  end;

  function TimePart: Boolean;
  var
    H, M, Sec, MSec: Word;
  begin
    MSec := 0;
    Result := ((TimeLength = 8) or (TimeLength = 12)) and (S[TimeStart + 2] = ':') and (S[TimeStart + 5] = ':') and
      Number(TimeStart, 2, H) and Number(TimeStart + 3, 2, M) and Number(TimeStart + 6, 2, Sec);
    if Result and (TimeLength = 12) then
      Result := (S[TimeStart + 8] = '.') and Number(TimeStart + 9, 3, MSec);
    Result := Result and TryEncodeTime(H, M, Sec, MSec, Time);
  end;

begin
  Value := 0;
  Day := 0;
  Time := 0;
  case DataType of
    ftDate:
      begin
        Result := (Length(S) = 10) and DatePart;
        Value := Day;
      end;
    ftTime:
      begin
        TimeStart := 1;
        TimeLength := Length(S);
        Result := TimePart;
        Value := Time;
      end;
  else
    TimeStart := 12;
    TimeLength := Length(S) - 11;
    Result := (Length(S) >= 19) and (S[11] = 'T') and DatePart and TimePart;
    { the time of day counts away from zero }
    if Day < 0 then
      Value := Day - Time
    else
      Value := Day + Time;
  end;
end;

function TTableReader.ReadBase64(const S: string; out Bytes: TBytes): Boolean;
var
  I, J, N, Pad, Count, Quad, Code: Integer;
  C: Char;
begin
  Result := False;
  Bytes := nil;
  N := Length(S);
  if N mod 4 <> 0 then
    Exit;
  Pad := 0;
  if (N > 0) and (S[N] = '=') then
  begin
    Pad := 1;
    if S[N - 1] = '=' then
      Pad := 2;
  end;
  Count := N div 4 * 3 - Pad;
  SetLength(Bytes, Count);
  J := 0;
  I := 1;
  while I <= N do
  begin
    Quad := 0;
    for Code := 0 to 3 do
    begin
      C := S[I + Code];
      if (C = '=') and (I + Code > N - Pad) then
        Quad := Quad shl 6
      else if (C >= 'A') and (C <= 'Z') then
        Quad := Quad shl 6 or (Ord(C) - Ord('A'))
      else if (C >= 'a') and (C <= 'z') then
        Quad := Quad shl 6 or (Ord(C) - Ord('a') + 26)
      else if (C >= '0') and (C <= '9') then
        Quad := Quad shl 6 or (Ord(C) - Ord('0') + 52)
      else if C = '+' then
        Quad := Quad shl 6 or 62
      else if C = '/' then
        Quad := Quad shl 6 or 63
      else
      begin
        Bytes := nil;
        Exit;
      end;
    end;
    if J < Count then
      Bytes[J] := (Quad shr 16) and 255;
    if J + 1 < Count then
      Bytes[J + 1] := (Quad shr 8) and 255;
    if J + 2 < Count then
      Bytes[J + 2] := Quad and 255;
    J := J + 3;
    I := I + 4;
  end;
  Result := True;
end;

{ One value of a row, converted to the stored form of its field type. }
function TTableReader.ReadValue(Def: TFieldDef): TDBValue;
var
  Start, Code: Integer;
  C: Char;
  Number, S: string;
  IsInteger: Boolean;
  N: Int64;
  D: Double;
  Bytes: TBytes;

  procedure WrongType;
  begin
    Fail('value of field "' + Def.Name + '" has the wrong type', Start - 1);
  end;

  procedure OutOfRange;
  begin
    Fail('value of field "' + Def.Name + '" is out of range', Start - 1);
  end;

begin
  SkipSpace;
  Start := FPos;
  C := Current;
  Result := NullValue;
  if C = 'n' then
  begin
    ReadLiteral('null');
    Exit;
  end;
  case Def.DataType of
    ftSmallint, ftInteger, ftWord, ftAutoInc, ftLargeint:
      begin
        if (C <> '-') and ((C < '0') or (C > '9')) then
          WrongType;
        Number := ReadNumber(IsInteger);
        if not IsInteger then
          WrongType;
        if not TryStrToInt64(Number, N) then
          OutOfRange;
        case Def.DataType of
          ftSmallint:
            if (N < -32768) or (N > 32767) then
              OutOfRange;
          ftWord:
            if (N < 0) or (N > 65535) then
              OutOfRange;
          ftInteger, ftAutoInc:
            if (N < Low(Integer)) or (N > High(Integer)) then
              OutOfRange;
        end;
        Result := IntValue(N);
      end;
    ftBoolean:
      if C = 't' then
      begin
        ReadLiteral('true');
        Result := IntValue(1);
      end
      else if C = 'f' then
      begin
        ReadLiteral('false');
        Result := IntValue(0);
      end
      else
        WrongType;
    ftFloat, ftCurrency:
      begin
        if (C <> '-') and ((C < '0') or (C > '9')) then
          WrongType;
        Number := ReadNumber(IsInteger);
        { the sign apart: a negative zero keeps its sign }
        if C = '-' then
          Number := Copy(Number, 2, Length(Number) - 1);
        Val(Number, D, Code);
        if (Code <> 0) or (D > 1.7976931348623157E308) then
          OutOfRange;
        if C = '-' then
          D := -D;
        Result := FloatValue(D);
      end;
    ftString, ftMemo:
      begin
        if C <> '"' then
          WrongType;
        S := ReadString;
        if (Def.DataType = ftString) and (Def.Size > 0) and (Length(S) > Def.Size) then
          OutOfRange;
        Result := StringValue(S);
      end;
    ftDate, ftTime, ftDateTime:
      begin
        if C <> '"' then
          WrongType;
        S := ReadString;
        if not ReadDateTime(Def.DataType, S, D) then
          Fail('invalid date/time in field "' + Def.Name + '"', Start - 1);
        Result := FloatValue(D);
      end;
    ftBlob:
      begin
        if C <> '"' then
          WrongType;
        S := ReadString;
        if not ReadBase64(S, Bytes) then
          Fail('invalid base64 in field "' + Def.Name + '"', Start - 1);
        Result := BytesValue(Bytes);
      end;
  else
    WrongType;
  end;
end;

procedure TTableReader.ReadTable(Defs: TFieldDefs; Rows: TDataRowList);
var
  Key, Version, Number: string;
  ValuePos, VersionPos, VersionEnd, FieldsPos, AutoIncPos, RowsPos, ClosePos, RowPos, Count, I, Index: Integer;
  Row: TDataRow;
  IsInteger: Boolean;
  Given: array of Boolean;
  N: Int64;
begin
  { the whole text: syntax, and where the known members are (the last one of a name counts) }
  SkipSpace;
  if Current <> '{' then
    SyntaxError;
  FPos := FPos + 1;
  VersionPos := 0;
  VersionEnd := 0;
  FieldsPos := 0;
  AutoIncPos := 0;
  RowsPos := 0;
  SkipSpace;
  ClosePos := FPos;
  if not EmptyGroup('}') then
    repeat
      Key := ReadName;
      ValuePos := FPos;
      SkipValue(1);
      if Key = 'formatVersion' then
      begin
        VersionPos := ValuePos;
        VersionEnd := FPos;
      end
      else if Key = 'fields' then
        FieldsPos := ValuePos
      else if Key = 'autoInc' then
        AutoIncPos := ValuePos
      else if Key = 'rows' then
        RowsPos := ValuePos;
      SkipSpace;
      ClosePos := FPos;
    until not More('}');
  SkipSpace;
  if not AtEnd then
    SyntaxError;

  if VersionPos = 0 then
    Fail('unsupported formatVersion null', ClosePos - 1);
  Version := Copy(FText, VersionPos, VersionEnd - VersionPos);
  if Version <> '1' then
    Fail('unsupported formatVersion ' + Version, VersionPos - 1);

  if FieldsPos = 0 then
    Fail('missing "fields"', ClosePos - 1);
  FPos := FieldsPos;
  if Current <> '[' then
    SyntaxError;
  FPos := FPos + 1;
  if EmptyGroup(']') then
    Fail('missing "fields"', FieldsPos - 1);
  repeat
    ReadField(Defs);
  until not More(']');

  if RowsPos > 0 then
  begin
    FPos := RowsPos;
    if Current <> '[' then
      SyntaxError;
    FPos := FPos + 1;
    if not EmptyGroup(']') then
      repeat
        SkipSpace;
        RowPos := FPos;
        if Current <> '[' then
          SyntaxError;
        { the number of values first, then the values }
        FPos := FPos + 1;
        Count := 0;
        if not EmptyGroup(']') then
          repeat
            SkipValue(0);
            Count := Count + 1;
          until not More(']');
        if Count <> Defs.Count then
          Fail('row has ' + IntToStr(Count) + ' values, expected ' + IntToStr(Defs.Count), RowPos - 1);
        FPos := RowPos + 1;
        Row := TDataRow.Create(Count);
        Rows.Add(Row);
        I := 0;
        repeat
          Row.Values[I] := ReadValue(Defs[I]);
          I := I + 1;
        until not More(']');
      until not More(']');
  end;

  { the counters: as given, else the highest loaded value + 1 }
  SetLength(Given, Defs.Count);
  SetLength(NextValues, Defs.Count);
  for I := 0 to Defs.Count - 1 do
  begin
    Given[I] := False;
    NextValues[I] := 1;
  end;
  if AutoIncPos > 0 then
  begin
    FPos := AutoIncPos;
    if Current <> '{' then
      SyntaxError;
    FPos := FPos + 1;
    if not EmptyGroup('}') then
      repeat
        Key := ReadName;
        ValuePos := FPos;
        Index := Defs.IndexOf(Key);
        if (Index >= 0) and (Defs[Index].DataType = ftAutoInc) then
        begin
          IsInteger := False;
          if (Current = '-') or ((Current >= '0') and (Current <= '9')) then
            Number := ReadNumber(IsInteger);
          if not IsInteger then
            Fail('value of field "' + Defs[Index].Name + '" has the wrong type', ValuePos - 1);
          if not TryStrToInt64(Number, N) then
            Fail('value of field "' + Defs[Index].Name + '" is out of range', ValuePos - 1);
          NextValues[Index] := N;
          Given[Index] := True;
        end
        else
          SkipValue(0);
      until not More('}');
  end;
  for I := 0 to Defs.Count - 1 do
    if (Defs[I].DataType = ftAutoInc) and not Given[I] then
      for Index := 0 to Rows.Count - 1 do
        if (Rows[Index].Values[I].Kind = vkInt) and (Rows[Index].Values[I].IntVal >= NextValues[I]) then
          NextValues[I] := Rows[Index].Values[I].IntVal + 1;
end;

{ TMemTable }

constructor TMemTable.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FStore := TDataRowList.Create(True);
end;

destructor TMemTable.Destroy;
begin
  FLoadDefs.Free;
  FLoadDefs := nil;
  FStore.Free;
  FStore := nil;
  inherited Destroy;
end;

{ The rows of the open table move into the store (as copies; the base class releases its rows). }
procedure TMemTable.StoreRows;
var
  I: Integer;
begin
  if not FLive or (FStore = nil) then
    Exit;
  FLive := False;
  FStore.Clear;
  for I := 0 to Rows.Count - 1 do
    FStore.Add(CopyRow(Rows[I]));
end;

procedure TMemTable.DiscardRows;
begin
  if FStore <> nil then
    FStore.Clear;
  SetLength(FNextValues, 0);
end;

procedure TMemTable.NeedCounters;
var
  I: Integer;
begin
  if Length(FNextValues) = FieldDefs.Count then
    Exit;
  SetLength(FNextValues, FieldDefs.Count);
  for I := 0 to High(FNextValues) do
    FNextValues[I] := 1;
end;

procedure TMemTable.DefsChanged;
begin
  if not FKeepRows then
    DiscardRows;
end;

procedure TMemTable.ReplaceDefs(Source: TFieldDefs);
var
  I: Integer;
begin
  FKeepRows := True;
  try
    FieldDefs.Clear;
    for I := 0 to Source.Count - 1 do
      FieldDefs.Add(Source[I].Name, Source[I].DataType, Source[I].Size, Source[I].Required);
  finally
    FKeepRows := False;
  end;
end;

function TMemTable.DataFieldCount: Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to Fields.Count - 1 do
    if Fields[I].FieldKind = fkData then
      Result := Result + 1;
end;

procedure TMemTable.InternalClose;
begin
  StoreRows;
  inherited InternalClose;
end;

{ Nothing to reload: the rows go through the store and come back. }
procedure TMemTable.InternalRefresh;
begin
  StoreRows;
  inherited InternalRefresh;
end;

procedure TMemTable.DriverOpen;
var
  Reader: TTableReader;
  I: Integer;
begin
  if FLoading then
  begin
    FLoadDefs.Free;
    FLoadDefs := TFieldDefs.Create(nil);
    Reader := TTableReader.Create(FLoadText);
    try
      Reader.ReadTable(FLoadDefs, FStore);
      SetLength(FNextValues, FLoadDefs.Count);
      for I := 0 to High(FNextValues) do
        FNextValues[I] := Reader.NextValues[I];
    finally
      Reader.Free;
    end;
  end
  else if (FieldDefs.Count = 0) and (DataFieldCount = 0) then
    DatabaseError(SMemNoFieldDefs);
end;

procedure TMemTable.DriverInitFieldDefs;
var
  I: Integer;
  F: TField;
begin
  if FLoadDefs <> nil then
  begin
    ReplaceDefs(FLoadDefs);
    FLoadDefs.Free;
    FLoadDefs := nil;
  end
  else if FieldDefs.Count = 0 then
  begin
    { the definitions of the persistent data fields }
    FKeepRows := True;
    try
      for I := 0 to Fields.Count - 1 do
      begin
        F := Fields[I];
        if F.FieldKind = fkData then
          FieldDefs.Add(F.FieldName, F.DataType, F.Size, F.Required);
      end;
    finally
      FKeepRows := False;
    end;
  end;
end;

procedure TMemTable.DriverLoadRows;
var
  I, J: Integer;
  Source, Row: TDataRow;
begin
  NeedCounters;
  for I := 0 to FStore.Count - 1 do
  begin
    Source := FStore[I];
    Row := AddLoadedRow;
    for J := 0 to High(Row.Values) do
      if J <= High(Source.Values) then
        Row.Values[J] := Source.Values[J];
  end;
  FStore.Clear;
  FLive := True;
end;

procedure TMemTable.DriverClose;
begin
end;

function TMemTable.DriverCanModify: Boolean;
begin
  Result := not FReadOnly;
end;

{ An auto-increment column that is null takes the next value of its counter; an assigned value moves the
  counter past it. }
procedure TMemTable.ApplyInsert(Buffer: TDataRow);
var
  I: Integer;
begin
  NeedCounters;
  for I := 0 to FieldDefs.Count - 1 do
    if FieldDefs[I].DataType = ftAutoInc then
      if Buffer.Values[I].Kind = vkNull then
      begin
        Buffer.Values[I] := IntValue(FNextValues[I]);
        FNextValues[I] := FNextValues[I] + 1;
      end
      else if (Buffer.Values[I].Kind = vkInt) and (Buffer.Values[I].IntVal >= FNextValues[I]) then
        FNextValues[I] := Buffer.Values[I].IntVal + 1;
end;

procedure TMemTable.ApplyUpdate(OldRow, Buffer: TDataRow);
begin
end;

procedure TMemTable.ApplyDelete(Row: TDataRow);
begin
end;

procedure TMemTable.CreateDataSet;
begin
  Close;
  DiscardRows;
  Open;
end;

procedure TMemTable.EmptyDataSet;
begin
  CheckActive;
  if State in [dsEdit, dsInsert] then
    Cancel;
  ClearRows;
  SetLength(FNextValues, 0);
  NeedCounters;
  RowsReplaced(True);
end;

procedure TMemTable.CopyDataSet(Source: TDataSet);
var
  Defs: TFieldDefs;
  Copied: TDataRowList;
  SourceFields: TList;
  I, J: Integer;
  F: TField;
  Row: TDataRow;
  DataType: TFieldType;
begin
  if (Source = nil) or not Source.Active then
    DatabaseError(SDatClosed);
  Defs := TFieldDefs.Create(nil);
  Copied := TDataRowList.Create(True);
  SourceFields := TList.Create;
  try
    { everything is read before this table changes: the source may be this table }
    for I := 0 to Source.Fields.Count - 1 do
    begin
      F := Source.Fields[I];
      if F.FieldKind = fkData then
      begin
        DataType := F.DataType;
        if DataType = ftAutoInc then
          DataType := ftInteger;
        Defs.Add(F.FieldName, DataType, F.Size, F.Required);
        SourceFields.Add(F);
      end;
    end;
    for I := 0 to Source.RecordCount - 1 do
    begin
      Row := TDataRow.Create(SourceFields.Count);
      Copied.Add(Row);
      for J := 0 to SourceFields.Count - 1 do
        Row.Values[J] := ViewValue(Source, I, TField(SourceFields[J]));
    end;
    Close;
    FieldDefs.Clear;
    ReplaceDefs(Defs);
    Open;
    for I := 0 to Copied.Count - 1 do
    begin
      Row := AddLoadedRow;
      for J := 0 to High(Row.Values) do
        Row.Values[J] := Copied[I].Values[J];
    end;
    RowsReplaced(False);
  finally
    SourceFields.Free;
    Copied.Free;
    Defs.Free;
  end;
end;

function TMemTable.SaveToJSON: string;
var
  B: TStringBuilder;
  I, J: Integer;
  First: Boolean;
  Row: TDataRow;
begin
  CheckActive;
  CheckBrowseMode;
  NeedCounters;
  B := TStringBuilder.Create;
  try
    B.Append('{' + LF + '  "formatVersion": 1,' + LF + '  "fields": [' + LF);
    for I := 0 to FieldDefs.Count - 1 do
    begin
      B.Append('    { "name": ');
      AppendString(B, FieldDefs[I].Name);
      B.Append(', "type": "' + TypeNames[FieldDefs[I].DataType] + '", "size": ' + IntToStr(FieldDefs[I].Size) +
        ', "required": ');
      if FieldDefs[I].Required then
        B.Append('true }')
      else
        B.Append('false }');
      if I < FieldDefs.Count - 1 then
        B.Append(',');
      B.Append(LF);
    end;
    B.Append('  ],' + LF + '  "autoInc": {');
    First := True;
    for I := 0 to FieldDefs.Count - 1 do
      if FieldDefs[I].DataType = ftAutoInc then
      begin
        if First then
          B.Append(' ')
        else
          B.Append(', ');
        First := False;
        AppendString(B, FieldDefs[I].Name);
        B.Append(': ' + IntToStr(FNextValues[I]));
      end;
    if not First then
      B.Append(' ');
    B.Append('},' + LF + '  "rows": [');
    for I := 0 to Rows.Count - 1 do
    begin
      Row := Rows[I];
      B.Append(LF + '    [');
      for J := 0 to FieldDefs.Count - 1 do
      begin
        if J > 0 then
          B.Append(', ');
        AppendValue(B, FieldDefs[J], Row.Values[J]);
      end;
      B.Append(']');
      if I < Rows.Count - 1 then
        B.Append(',');
    end;
    if Rows.Count > 0 then
      B.Append(LF + '  ');
    B.Append(']' + LF + '}' + LF);
    Result := B.ToString;
  finally
    B.Free;
  end;
end;

procedure TMemTable.SaveToStream(Stream: TStream);
var
  Bytes: TBytes;
begin
  Bytes := TEncoding.UTF8.GetBytes(SaveToJSON);
  if Length(Bytes) > 0 then
    Stream.WriteBuffer(Bytes[0], Length(Bytes));
end;

procedure TMemTable.SaveToFile(const FileName: string);
var
  Bytes: TBytes;
  Stream: TFileStream;
begin
  { the text first: a failing save leaves an existing file alone }
  Bytes := TEncoding.UTF8.GetBytes(SaveToJSON);
  Stream := TFileStream.Create(FileName, fmCreate);
  try
    if Length(Bytes) > 0 then
      Stream.WriteBuffer(Bytes[0], Length(Bytes));
  finally
    Stream.Free;
  end;
end;

procedure TMemTable.LoadFromJSON(const JSON: string);
begin
  Close;
  DiscardRows;
  FLoadText := JSON;
  FLoading := True;
  try
    try
      Open;
    finally
      FLoading := False;
      FLoadText := '';
      FLoadDefs.Free;
      FLoadDefs := nil;
    end;
  except
    { closed and empty; an error of an event handler after the load leaves the loaded table open }
    if not Active then
      DiscardRows;
    raise;
  end;
end;

{ UTF-8 bytes; a byte order mark at the start is skipped. }
procedure TMemTable.LoadBytes(const Bytes: TBytes);
var
  Start: Integer;
begin
  Start := 0;
  if (Length(Bytes) >= 3) and (Bytes[0] = $EF) and (Bytes[1] = $BB) and (Bytes[2] = $BF) then
    Start := 3;
  LoadFromJSON(TEncoding.UTF8.GetString(Bytes, Start, Length(Bytes) - Start));
end;

function ReadRest(Stream: TStream): TBytes;
var
  Count: Integer;
begin
  Count := Integer(Stream.Size - Stream.Position);
  SetLength(Result, Count);
  if Count > 0 then
    Stream.ReadBuffer(Result[0], Count);
end;

procedure TMemTable.LoadFromStream(Stream: TStream);
begin
  LoadBytes(ReadRest(Stream));
end;

procedure TMemTable.LoadFromFile(const FileName: string);
var
  Stream: TFileStream;
  Bytes: TBytes;
begin
  { the file is read and closed before the table changes }
  Stream := TFileStream.Create(FileName, fmOpenRead or fmShareDenyWrite);
  try
    Bytes := ReadRest(Stream);
  finally
    Stream.Free;
  end;
  LoadBytes(Bytes);
end;

initialization
  RegisterClasses([TMemTable]);

end.
