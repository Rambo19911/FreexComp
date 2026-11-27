// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ NetEncoding - encodings that carry bytes or text through channels made for plain text (docs: rtl/other-units.md
  section 12, RTL-1200). This unit holds the base class and the Base64 family of RFC 4648: the standard and the
  URL-safe alphabet, optional lines, tolerant and strict decoding.
  Pure Object Pascal: nothing here calls the operating system. No object of this unit changes after its constructor
  has returned, so one object may be used from several threads at once; the shared instances behind the class
  properties are created on first use and freed when the program ends - a program never frees them. }
{$Q-,R-}
unit NetEncoding;

interface

uses
  SysUtils, Classes;

type
  { Base of every encoding. The public methods are fixed; each one calls the protected method of the same name with
    "Do" in front. A descendant overrides DoEncode(string) and DoDecode(string): the other forms are built on these
    two here, reading bytes as UTF-8 text and returning text as its UTF-8 form. }
  TNetEncoding = class
  private
    class function GetBase64: TNetEncoding; static;
    class function GetBase64String: TNetEncoding; static;
    class function GetBase64URL: TNetEncoding; static;
  protected
    function DoEncode(const Input, Output: TStream): Integer; overload; virtual;
    function DoEncode(const Input: array of Byte): TBytes; overload; virtual;
    function DoEncode(const Input: string): string; overload; virtual; abstract;
    function DoDecode(const Input, Output: TStream): Integer; overload; virtual;
    function DoDecode(const Input: array of Byte): TBytes; overload; virtual;
    function DoDecode(const Input: string): string; overload; virtual; abstract;
    function DoEncodeBytesToString(const Input: array of Byte): string; overload; virtual;
    function DoEncodeBytesToString(const Input: Pointer; Size: Integer): string; overload; virtual;
    function DoDecodeStringToBytes(const Input: string): TBytes; virtual;
  public
    { Reads Input from its position to its end, writes at the position of Output; the result is the number of bytes
      written. }
    function Encode(const Input, Output: TStream): Integer; overload;
    function Encode(const Input: array of Byte): TBytes; overload;
    function Encode(const Input: string): string; overload;
    function Decode(const Input, Output: TStream): Integer; overload;
    function Decode(const Input: array of Byte): TBytes; overload;
    function Decode(const Input: string): string; overload;
    function EncodeBytesToString(const Input: array of Byte): string; overload;
    function EncodeBytesToString(const Input: Pointer; Size: Integer): string; overload;
    function DecodeStringToBytes(const Input: string): TBytes;
    { Shared instances: Base64 breaks its text into lines of 76 characters with CR LF between them, Base64String
      writes one line, Base64URL uses the URL-safe alphabet and writes no pad characters. All three decode tolerantly. }
    class property Base64: TNetEncoding read GetBase64;
    class property Base64String: TNetEncoding read GetBase64String;
    class property Base64URL: TNetEncoding read GetBase64URL;
  end;

  { bdTolerant: characters outside the alphabet are skipped, the first pad character ends the data, nothing raises.
    bdStrict: only white space is skipped; a foreign character, a length no encoder produces, wrong or missing
    padding and pad bits other than 0 raise EConvertError. }
  TBase64DecodeMode = (bdTolerant, bdStrict);

  { Base64: three bytes become four characters. Encode(string) encodes the UTF-8 form of the text and Decode(string)
    returns the decoded bytes as UTF-8 text; the byte forms take and return the bytes themselves.
    CharsPerLine = 0 writes one line; otherwise LineSeparator stands between lines of CharsPerLine characters and
    never at the end. A separator consists of spaces, tabs, CR and LF only. Created directly, this class uses the
    standard alphabet with padding and writes one line. }
  TCustomBase64Encoding = class(TNetEncoding)
  private
    FCharsPerLine: Integer;
    FLineSeparator: string;
    FDecodeMode: TBase64DecodeMode;
    FPadded: Boolean;                       { the encoder writes pad characters, the strict decoder insists on them }
    FAlphabet: array[0..63] of Byte;        { the character that stands for each value }
    FKind: array[0..127] of ShortInt;       { for each ASCII code: its value 0..63, or one of the ck constants }
    function UrlSafe: Boolean; virtual;
    function DefaultCharsPerLine: Integer; virtual;
    procedure Setup(ACharsPerLine: Integer; const ALineSeparator: string; ADecodeMode: TBase64DecodeMode);
    function EncodeToBytes(P: PByte; Count: Integer): TBytes;
    function DecodeText(const Input: string): TBytes;
  protected
    function DoEncode(const Input, Output: TStream): Integer; overload; override;
    function DoEncode(const Input: array of Byte): TBytes; overload; override;
    function DoEncode(const Input: string): string; overload; override;
    function DoDecode(const Input, Output: TStream): Integer; overload; override;
    function DoDecode(const Input: array of Byte): TBytes; overload; override;
    function DoDecode(const Input: string): string; overload; override;
    function DoEncodeBytesToString(const Input: array of Byte): string; overload; override;
    function DoEncodeBytesToString(const Input: Pointer; Size: Integer): string; overload; override;
    function DoDecodeStringToBytes(const Input: string): TBytes; override;
  public
    { Create alone takes the defaults of the class; a line length without a separator means CR LF }
    constructor Create; overload;
    constructor Create(CharsPerLine: Integer); overload;
    constructor Create(CharsPerLine: Integer; const LineSeparator: string); overload;
    constructor Create(CharsPerLine: Integer; const LineSeparator: string; DecodeMode: TBase64DecodeMode); overload;
    property CharsPerLine: Integer read FCharsPerLine;
    property LineSeparator: string read FLineSeparator;
    property DecodeMode: TBase64DecodeMode read FDecodeMode;
  end;

  { standard alphabet, padded, lines of 76 characters with CR LF between them }
  TBase64Encoding = class(TCustomBase64Encoding)
  private
    function DefaultCharsPerLine: Integer; override;
  end;

  { standard alphabet, padded, one line }
  TBase64StringEncoding = class(TCustomBase64Encoding);

  { URL-safe alphabet ("-" and "_" for the values 62 and 63), no pad characters, one line }
  TBase64URLEncoding = class(TCustomBase64Encoding)
  private
    function UrlSafe: Boolean; override;
  end;

implementation

uses
  RTLConsts;

const
  { the most a stream is asked for in one Read, and the most that is handed to one WriteBuffer }
  BlockSize = 65536;
  PadChar = Ord('=');
  { FKind entries that are not a value }
  ckForeign = -1;
  ckPad = -2;
  ckWhite = -3;

{ ---------------------------------------------------------------- argument checks and byte helpers }

procedure CheckStreams(Input, Output: TStream);
begin
  if (Input = nil) or (Output = nil) then
    raise EArgumentNilException.Create(SArgumentNil);
end;

procedure CheckBuffer(Input: Pointer; Size: Integer);
begin
  if Size < 0 then
    raise EArgumentOutOfRangeException.Create(SArgumentOutOfRange);
  if (Size > 0) and (Input = nil) then
    raise EArgumentNilException.Create(SArgumentNil);
end;

function CopyBytes(P: PByte; Count: Integer): TBytes;
begin
  SetLength(Result, Count);
  if Count > 0 then
    Move(P^, Result[0], Count);
end;

function OpenArrayBytes(const Input: array of Byte): TBytes;
begin
  if Length(Input) > 0 then
    Result := CopyBytes(@Input[0], Length(Input))
  else
    Result := nil;
end;

{ one character per byte }
function BytesAsText(const Bytes: TBytes): string;
var
  I: Integer;
begin
  SetLength(Result, Length(Bytes));
  for I := 0 to Length(Bytes) - 1 do
    Result[I + 1] := Char(Bytes[I]);
end;

{ everything from the position of Input to its end; the size of the stream is not asked for }
function ReadRest(Input: TStream): TBytes;
var
  Total, N: Integer;
begin
  Result := nil;
  Total := 0;
  repeat
    if Length(Result) < Total + BlockSize then
      SetLength(Result, Total * 2 + BlockSize);
    N := Input.Read(Result[Total], BlockSize);
    if N > 0 then
      Total := Total + N;
  until N <= 0;
  SetLength(Result, Total);
end;

function WriteAll(Output: TStream; const Bytes: TBytes): Integer;
begin
  Result := Length(Bytes);
  if Result > 0 then
    Output.WriteBuffer(Bytes[0], Result);
end;

{ ---------------------------------------------------------------- shared instances (RTL-1202) }

const
  siBase64 = 0;
  siBase64String = 1;
  siBase64URL = 2;

var
  SharedInstances: array[siBase64..siBase64URL] of Pointer;

{ created on first use; of two threads that race, the one that loses the exchange frees its own object }
function SharedInstance(Index: Integer): TNetEncoding;
var
  Mine: TNetEncoding;
begin
  Result := TNetEncoding(SharedInstances[Index]);
  if Result <> nil then
    Exit;
  case Index of
    siBase64: Mine := TBase64Encoding.Create;
    siBase64String: Mine := TBase64StringEncoding.Create;
  else
    Mine := TBase64URLEncoding.Create;
  end;
  if AtomicCmpExchange(SharedInstances[Index], Pointer(Mine), nil) <> nil then
    Mine.Free;
  Result := TNetEncoding(SharedInstances[Index]);
end;

procedure FreeSharedInstances;
var
  I: Integer;
begin
  for I := siBase64 to siBase64URL do
  begin
    TNetEncoding(SharedInstances[I]).Free;
    SharedInstances[I] := nil;
  end;
end;

{ ---------------------------------------------------------------- TNetEncoding }

class function TNetEncoding.GetBase64: TNetEncoding;
begin
  Result := SharedInstance(siBase64);
end;

class function TNetEncoding.GetBase64String: TNetEncoding;
begin
  Result := SharedInstance(siBase64String);
end;

class function TNetEncoding.GetBase64URL: TNetEncoding;
begin
  Result := SharedInstance(siBase64URL);
end;

function TNetEncoding.Encode(const Input, Output: TStream): Integer;
begin
  Result := DoEncode(Input, Output);
end;

function TNetEncoding.Encode(const Input: array of Byte): TBytes;
begin
  Result := DoEncode(Input);
end;

function TNetEncoding.Encode(const Input: string): string;
begin
  Result := DoEncode(Input);
end;

function TNetEncoding.Decode(const Input, Output: TStream): Integer;
begin
  Result := DoDecode(Input, Output);
end;

function TNetEncoding.Decode(const Input: array of Byte): TBytes;
begin
  Result := DoDecode(Input);
end;

function TNetEncoding.Decode(const Input: string): string;
begin
  Result := DoDecode(Input);
end;

function TNetEncoding.EncodeBytesToString(const Input: array of Byte): string;
begin
  Result := DoEncodeBytesToString(Input);
end;

function TNetEncoding.EncodeBytesToString(const Input: Pointer; Size: Integer): string;
begin
  Result := DoEncodeBytesToString(Input, Size);
end;

function TNetEncoding.DecodeStringToBytes(const Input: string): TBytes;
begin
  Result := DoDecodeStringToBytes(Input);
end;

{ The forms below are those of an encoding from text to text (RTL-1203): bytes are UTF-8 text on the way in and the
  UTF-8 form of the result on the way out. An escape may lie across any block boundary, so the stream forms read
  the whole input first. }

function TNetEncoding.DoEncode(const Input, Output: TStream): Integer;
var
  Raw: TBytes;
begin
  CheckStreams(Input, Output);
  Raw := ReadRest(Input);
  Result := WriteAll(Output, DoEncode(Raw));
end;

function TNetEncoding.DoEncode(const Input: array of Byte): TBytes;
var
  Text: string;
begin
  Text := TEncoding.UTF8.GetString(OpenArrayBytes(Input));
  Result := TEncoding.UTF8.GetBytes(DoEncode(Text));
end;

function TNetEncoding.DoDecode(const Input, Output: TStream): Integer;
var
  Raw: TBytes;
begin
  CheckStreams(Input, Output);
  Raw := ReadRest(Input);
  Result := WriteAll(Output, DoDecode(Raw));
end;

function TNetEncoding.DoDecode(const Input: array of Byte): TBytes;
var
  Text: string;
begin
  Text := TEncoding.UTF8.GetString(OpenArrayBytes(Input));
  Result := TEncoding.UTF8.GetBytes(DoDecode(Text));
end;

function TNetEncoding.DoEncodeBytesToString(const Input: array of Byte): string;
var
  Text: string;
begin
  Text := TEncoding.UTF8.GetString(OpenArrayBytes(Input));
  Result := DoEncode(Text);
end;

function TNetEncoding.DoEncodeBytesToString(const Input: Pointer; Size: Integer): string;
var
  Raw: TBytes;
begin
  CheckBuffer(Input, Size);
  Raw := CopyBytes(Input, Size);
  Result := DoEncodeBytesToString(Raw);
end;

function TNetEncoding.DoDecodeStringToBytes(const Input: string): TBytes;
begin
  Result := TEncoding.UTF8.GetBytes(DoDecode(Input));
end;

{ ---------------------------------------------------------------- Base64: where the output goes }

type
  { Collects output bytes. With a stream every full block is written out and the buffer is used again; without one
    the buffer grows and holds the whole result. }
  TSink = record
    Buf: TBytes;
    Cap: Integer;        { Length(Buf) }
    Len: Integer;        { bytes of Buf in use }
    Stream: TStream;
    Written: Integer;    { bytes handed to Stream so far }
  end;

procedure SinkOpen(var Sink: TSink; Stream: TStream; Capacity: Integer);
begin
  if Capacity < 64 then
    Capacity := 64;
  SetLength(Sink.Buf, Capacity);
  Sink.Cap := Capacity;
  Sink.Len := 0;
  Sink.Stream := Stream;
  Sink.Written := 0;
end;

procedure SinkFlush(var Sink: TSink);
begin
  if Sink.Len > 0 then
    Sink.Stream.WriteBuffer(Sink.Buf[0], Sink.Len);
  Sink.Written := Sink.Written + Sink.Len;
  Sink.Len := 0;
end;

procedure SinkPut(var Sink: TSink; B: Byte);
begin
  if Sink.Len = Sink.Cap then
    if Sink.Stream <> nil then
      SinkFlush(Sink)
    else
    begin
      Sink.Cap := Sink.Cap * 2;
      SetLength(Sink.Buf, Sink.Cap);
    end;
  Sink.Buf[Sink.Len] := B;
  Inc(Sink.Len);
end;

{ the collected bytes of a sink without a stream }
function SinkBytes(var Sink: TSink): TBytes;
begin
  SetLength(Sink.Buf, Sink.Len);
  Result := Sink.Buf;
end;

{ ---------------------------------------------------------------- Base64: encoder and decoder }

type
  { what the encoder keeps between two blocks of input }
  TEncodeState = record
    Acc: Cardinal;       { the bytes of the group that is not complete yet }
    Count: Integer;      { how many they are: 0..2 }
    Column: Integer;     { characters written to the current line }
  end;

  { what the decoder keeps between two blocks of input }
  TDecodeState = record
    Acc: Cardinal;       { the values of the group that is not complete yet }
    Count: Integer;      { how many they are: 0..3 }
    Pads: Integer;       { pad characters seen }
    Position: Integer;   { characters or bytes taken so far: the position named in a message }
  end;

{ One output character; the separator goes in front of the character that opens a new line, so the text never
  ends with one (RTL-1206). }
procedure PutChar(Enc: TCustomBase64Encoding; var State: TEncodeState; var Sink: TSink; C: Byte);
var
  I: Integer;
begin
  if (Enc.FCharsPerLine > 0) and (State.Column = Enc.FCharsPerLine) then
  begin
    for I := 1 to Length(Enc.FLineSeparator) do
      SinkPut(Sink, Byte(Ord(Enc.FLineSeparator[I])));
    State.Column := 0;
  end;
  SinkPut(Sink, C);
  Inc(State.Column);
end;

procedure EncodeBytes(Enc: TCustomBase64Encoding; var State: TEncodeState; var Sink: TSink; P: PByte; Count: Integer);
var
  I: Integer;
begin
  for I := 1 to Count do
  begin
    State.Acc := (State.Acc shl 8) or P^;
    Inc(P);
    Inc(State.Count);
    if State.Count = 3 then
    begin
      PutChar(Enc, State, Sink, Enc.FAlphabet[(State.Acc shr 18) and 63]);
      PutChar(Enc, State, Sink, Enc.FAlphabet[(State.Acc shr 12) and 63]);
      PutChar(Enc, State, Sink, Enc.FAlphabet[(State.Acc shr 6) and 63]);
      PutChar(Enc, State, Sink, Enc.FAlphabet[State.Acc and 63]);
      State.Acc := 0;
      State.Count := 0;
    end;
  end;
end;

{ the last one or two bytes: the bits that are missing are 0 (RTL-1205) }
procedure EncodeEnd(Enc: TCustomBase64Encoding; var State: TEncodeState; var Sink: TSink);
begin
  if State.Count = 1 then
  begin
    PutChar(Enc, State, Sink, Enc.FAlphabet[(State.Acc shr 2) and 63]);
    PutChar(Enc, State, Sink, Enc.FAlphabet[(State.Acc shl 4) and 63]);
    if Enc.FPadded then
    begin
      PutChar(Enc, State, Sink, PadChar);
      PutChar(Enc, State, Sink, PadChar);
    end;
  end
  else if State.Count = 2 then
  begin
    PutChar(Enc, State, Sink, Enc.FAlphabet[(State.Acc shr 10) and 63]);
    PutChar(Enc, State, Sink, Enc.FAlphabet[(State.Acc shr 4) and 63]);
    PutChar(Enc, State, Sink, Enc.FAlphabet[(State.Acc shl 2) and 63]);
    if Enc.FPadded then
      PutChar(Enc, State, Sink, PadChar);
  end;
end;

{ One character of the input, or one byte of it (RTL-1208, RTL-1209). }
procedure DecodeUnit(Enc: TCustomBase64Encoding; var State: TDecodeState; var Sink: TSink; Code: Integer);
var
  Kind: Integer;
begin
  Inc(State.Position);
  if Code > 127 then
    Kind := ckForeign
  else
    Kind := Enc.FKind[Code];
  if Kind >= 0 then
  begin
    if State.Pads > 0 then
    begin
      { tolerant: the data ended at the first pad character; strict: nothing of the alphabet may follow one }
      if Enc.FDecodeMode = bdStrict then
        raise EConvertError.Create(SBase64InvalidPadding);
      Exit;
    end;
    State.Acc := (State.Acc shl 6) or Cardinal(Kind);
    Inc(State.Count);
    if State.Count = 4 then
    begin
      SinkPut(Sink, Byte(State.Acc shr 16));
      SinkPut(Sink, Byte(State.Acc shr 8));
      SinkPut(Sink, Byte(State.Acc));
      State.Acc := 0;
      State.Count := 0;
    end;
  end
  else if Kind = ckPad then
  begin
    Inc(State.Pads);
    if (State.Pads > 2) and (Enc.FDecodeMode = bdStrict) then
      raise EConvertError.Create(SBase64InvalidPadding);
  end
  else if (Kind = ckForeign) and (Enc.FDecodeMode = bdStrict) then
    raise EConvertError.CreateFmt(SBase64InvalidChar, [State.Position]);
end;

{ the rest of fewer than four characters: 2 give one byte, 3 give two, 1 gives nothing }
procedure DecodeEnd(Enc: TCustomBase64Encoding; var State: TDecodeState; var Sink: TSink);
var
  Required: Integer;
begin
  if Enc.FDecodeMode = bdStrict then
  begin
    if State.Count = 1 then
      raise EConvertError.Create(SBase64InvalidLength);
    Required := (4 - State.Count) mod 4;
    if (State.Pads <> Required) and (Enc.FPadded or (State.Pads <> 0)) then
      raise EConvertError.Create(SBase64InvalidPadding);
    { bits of the last character that belong to no byte }
    if ((State.Count = 2) and (State.Acc and 15 <> 0)) or ((State.Count = 3) and (State.Acc and 3 <> 0)) then
      raise EConvertError.Create(SBase64InvalidPadding);
  end;
  if State.Count = 2 then
    SinkPut(Sink, Byte(State.Acc shr 4))
  else if State.Count = 3 then
  begin
    SinkPut(Sink, Byte(State.Acc shr 10));
    SinkPut(Sink, Byte(State.Acc shr 2));
  end;
end;

procedure ResetEncode(var State: TEncodeState);
begin
  State.Acc := 0;
  State.Count := 0;
  State.Column := 0;
end;

procedure ResetDecode(var State: TDecodeState);
begin
  State.Acc := 0;
  State.Count := 0;
  State.Pads := 0;
  State.Position := 0;
end;

function IsWhite(C: Char): Boolean;
begin
  Result := (C = ' ') or (C = #9) or (C = #13) or (C = #10);
end;

{ ---------------------------------------------------------------- TCustomBase64Encoding }

function TCustomBase64Encoding.UrlSafe: Boolean;
begin
  Result := False;
end;

function TCustomBase64Encoding.DefaultCharsPerLine: Integer;
begin
  Result := 0;
end;

procedure TCustomBase64Encoding.Setup(ACharsPerLine: Integer; const ALineSeparator: string;
  ADecodeMode: TBase64DecodeMode);
const
  Letters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
var
  I: Integer;
begin
  if ACharsPerLine < 0 then
    raise EArgumentOutOfRangeException.Create(SArgumentOutOfRange);
  { the decoder of this object must be able to skip what its encoder inserts }
  for I := 1 to Length(ALineSeparator) do
    if not IsWhite(ALineSeparator[I]) then
      raise EArgumentException.Create(SInvalidArgument);
  FCharsPerLine := ACharsPerLine;
  FLineSeparator := ALineSeparator;
  FDecodeMode := ADecodeMode;
  FPadded := not UrlSafe;
  for I := 0 to 61 do
    FAlphabet[I] := Byte(Ord(Letters[I + 1]));
  if UrlSafe then
  begin
    FAlphabet[62] := Ord('-');
    FAlphabet[63] := Ord('_');
  end
  else
  begin
    FAlphabet[62] := Ord('+');
    FAlphabet[63] := Ord('/');
  end;
  for I := 0 to 127 do
    FKind[I] := ckForeign;
  FKind[32] := ckWhite;
  FKind[9] := ckWhite;
  FKind[13] := ckWhite;
  FKind[10] := ckWhite;
  FKind[PadChar] := ckPad;
  for I := 0 to 63 do
    FKind[FAlphabet[I]] := ShortInt(I);
end;

constructor TCustomBase64Encoding.Create;
begin
  inherited Create;
  if DefaultCharsPerLine > 0 then
    Setup(DefaultCharsPerLine, #13#10, bdTolerant)
  else
    Setup(0, '', bdTolerant);
end;

constructor TCustomBase64Encoding.Create(CharsPerLine: Integer);
begin
  inherited Create;
  Setup(CharsPerLine, #13#10, bdTolerant);
end;

constructor TCustomBase64Encoding.Create(CharsPerLine: Integer; const LineSeparator: string);
begin
  inherited Create;
  Setup(CharsPerLine, LineSeparator, bdTolerant);
end;

constructor TCustomBase64Encoding.Create(CharsPerLine: Integer; const LineSeparator: string;
  DecodeMode: TBase64DecodeMode);
begin
  inherited Create;
  Setup(CharsPerLine, LineSeparator, DecodeMode);
end;

function TCustomBase64Encoding.EncodeToBytes(P: PByte; Count: Integer): TBytes;
var
  State: TEncodeState;
  Sink: TSink;
  Chars: Integer;
begin
  { room for the characters and the separators; the sink grows should this be too little }
  Chars := (Count div 3) * 4 + 4;
  if FCharsPerLine > 0 then
    Chars := Chars + (Chars div FCharsPerLine) * Length(FLineSeparator);
  SinkOpen(Sink, nil, Chars);
  ResetEncode(State);
  EncodeBytes(Self, State, Sink, P, Count);
  EncodeEnd(Self, State, Sink);
  Result := SinkBytes(Sink);
end;

function TCustomBase64Encoding.DecodeText(const Input: string): TBytes;
var
  State: TDecodeState;
  Sink: TSink;
  I: Integer;
begin
  SinkOpen(Sink, nil, (Length(Input) div 4) * 3 + 3);
  ResetDecode(State);
  for I := 1 to Length(Input) do
    DecodeUnit(Self, State, Sink, Ord(Input[I]));
  DecodeEnd(Self, State, Sink);
  Result := SinkBytes(Sink);
end;

{ RTL-1204: blocks of at most BlockSize in and out; between two blocks only the state record is kept }
function TCustomBase64Encoding.DoEncode(const Input, Output: TStream): Integer;
var
  State: TEncodeState;
  Sink: TSink;
  Block: TBytes;
  N: Integer;
begin
  CheckStreams(Input, Output);
  SetLength(Block, BlockSize);
  SinkOpen(Sink, Output, BlockSize);
  ResetEncode(State);
  repeat
    N := Input.Read(Block[0], BlockSize);
    if N > 0 then
      EncodeBytes(Self, State, Sink, @Block[0], N);
  until N <= 0;
  EncodeEnd(Self, State, Sink);
  SinkFlush(Sink);
  Result := Sink.Written;
end;

function TCustomBase64Encoding.DoEncode(const Input: array of Byte): TBytes;
begin
  if Length(Input) > 0 then
    Result := EncodeToBytes(@Input[0], Length(Input))
  else
    Result := nil;
end;

function TCustomBase64Encoding.DoEncode(const Input: string): string;
var
  Raw: TBytes;
begin
  Raw := TEncoding.UTF8.GetBytes(Input);
  Result := BytesAsText(EncodeToBytes(Pointer(Raw), Length(Raw)));
end;

function TCustomBase64Encoding.DoDecode(const Input, Output: TStream): Integer;
var
  State: TDecodeState;
  Sink: TSink;
  Block: TBytes;
  I, N: Integer;
begin
  CheckStreams(Input, Output);
  SetLength(Block, BlockSize);
  SinkOpen(Sink, Output, BlockSize);
  ResetDecode(State);
  repeat
    N := Input.Read(Block[0], BlockSize);
    for I := 0 to N - 1 do
      DecodeUnit(Self, State, Sink, Block[I]);
  until N <= 0;
  DecodeEnd(Self, State, Sink);
  SinkFlush(Sink);
  Result := Sink.Written;
end;

function TCustomBase64Encoding.DoDecode(const Input: array of Byte): TBytes;
var
  State: TDecodeState;
  Sink: TSink;
  I: Integer;
begin
  SinkOpen(Sink, nil, (Length(Input) div 4) * 3 + 3);
  ResetDecode(State);
  for I := 0 to Length(Input) - 1 do
    DecodeUnit(Self, State, Sink, Input[I]);
  DecodeEnd(Self, State, Sink);
  Result := SinkBytes(Sink);
end;

function TCustomBase64Encoding.DoDecode(const Input: string): string;
begin
  Result := TEncoding.UTF8.GetString(DecodeText(Input));
end;

function TCustomBase64Encoding.DoEncodeBytesToString(const Input: array of Byte): string;
begin
  if Length(Input) > 0 then
    Result := BytesAsText(EncodeToBytes(@Input[0], Length(Input)))
  else
    Result := '';
end;

function TCustomBase64Encoding.DoEncodeBytesToString(const Input: Pointer; Size: Integer): string;
begin
  CheckBuffer(Input, Size);
  Result := BytesAsText(EncodeToBytes(Input, Size));
end;

function TCustomBase64Encoding.DoDecodeStringToBytes(const Input: string): TBytes;
begin
  Result := DecodeText(Input);
end;

{ ---------------------------------------------------------------- the three Base64 classes }

function TBase64Encoding.DefaultCharsPerLine: Integer;
begin
  Result := 76;
end;

function TBase64URLEncoding.UrlSafe: Boolean;
begin
  Result := True;
end;

initialization

finalization
  FreeSharedInstances;
end.
