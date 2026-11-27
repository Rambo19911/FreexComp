// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ NetEncoding - encodings that carry bytes or text through channels made for plain text (docs: rtl/other-units.md
  section 12, RTL-1200). This unit holds the base class, the Base64 family of RFC 4648 (the standard and the
  URL-safe alphabet, optional lines, tolerant and strict decoding), the percent-encoding of RFC 3986 for the parts
  of a URL and for form fields, and the escaping of the characters that have a meaning in HTML.
  Pure Object Pascal: nothing here calls the operating system. No object of this unit changes after its constructor
  has returned, so one object may be used from several threads at once; the shared instances behind the class
  properties are created on first use and freed when the program ends - a program never frees them. }
{$Q-,R-}
unit NetEncoding;

interface

uses
  SysUtils, Classes;

type
  TURLEncoding = class;

  { Base of every encoding. The public methods are fixed; each one calls the protected method of the same name with
    "Do" in front. A descendant overrides DoEncode(string) and DoDecode(string): the other forms are built on these
    two here, reading bytes as UTF-8 text and returning text as its UTF-8 form. }
  TNetEncoding = class
  private
    class function GetBase64: TNetEncoding; static;
    class function GetBase64String: TNetEncoding; static;
    class function GetBase64URL: TNetEncoding; static;
    class function GetURL: TURLEncoding; static;
    class function GetHTML: TNetEncoding; static;
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
    { URL escapes with "%" and two hex digits, HTML with character references; both turn text into text }
    class property URL: TURLEncoding read GetURL;
    class property HTML: TNetEncoding read GetHTML;
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

  { Percent-encoding (RFC 3986 section 2). The input of every Encode... method is raw text: a character that is not
    kept is turned into the bytes of the encoding (UTF-8 unless one is given) and each byte is written as "%" and
    two upper-case hex digits. All four component methods encode "%", so decoding once gives the raw text back.
      EncodePath   a whole path: keeps letters, digits, - . _ ~ ! $ & ' ( ) * + , ; = : @ and "/" between segments
      EncodeAuth   a user name or a password: as EncodePath without : @ /
      EncodeQuery  one name or one value of a query: keeps letters, digits and - . _ ~ only
      EncodeForm   one name or one value of a form: keeps letters, digits and * - . _ and writes "+" for a space
    AExtraUnsafeChars names further characters to encode. Encode and Decode with one parameter are EncodeQuery and
    URLDecode. Decoding raises EConvertError for a "%" that is not followed by two hex digits; bytes that are not
    valid in the encoding become U+FFFD. }
  TURLEncoding = class(TNetEncoding)
  protected
    function DoEncode(const Input: string): string; overload; override;
    function DoDecode(const Input: string): string; overload; override;
  public
    type
      UnsafeChar = 32..127;
      TUnsafeChars = set of UnsafeChar;
      TEncodeOption = (SpacesAsPlus, EncodePercent);
      TEncodeOptions = set of TEncodeOption;
      TDecodeOption = (PlusAsSpaces);
      TDecodeOptions = set of TDecodeOption;
    function EncodePath(const APath: string; const AExtraUnsafeChars: TUnsafeChars = []): string;
    function EncodeAuth(const AAuth: string; const AExtraUnsafeChars: TUnsafeChars = []): string;
    function EncodeQuery(const AQuery: string; const AExtraUnsafeChars: TUnsafeChars = []): string;
    function EncodeForm(const AInput: string; const AExtraUnsafeChars: TUnsafeChars = [];
      AEncoding: TEncoding = nil): string;
    { "+" stays "+" }
    function URLDecode(const AValue: string): string;
    { "+" is a space }
    function FormDecode(const AValue: string; AEncoding: TEncoding = nil): string;
    { The general forms. Encode: a space becomes "+" with SpacesAsPlus; "%" is encoded with EncodePercent or when
      it is in ASet, and copied otherwise (for text that holds escapes already); the other characters from U+0020
      to U+007F are encoded when they are in ASet; every character outside that range is always encoded. }
    function Encode(const AInput: string; const ASet: TUnsafeChars; const AOptions: TEncodeOptions;
      AEncoding: TEncoding = nil): string; overload;
    function Decode(const AValue: string; const AOptions: TDecodeOptions; AEncoding: TEncoding = nil): string; overload;
  end;

  { HTML character escaping. Encode replaces & < > " and ' (the last one by "&#39;") and copies everything else.
    Decode replaces "&name;" for the 252 names of HTML 4.01 and "apos", "&#decimal;" and "&#xhex;"; names are
    case-sensitive and the ";" is needed. Whatever is not such a reference is copied: Decode never raises. A number
    that is no character (0, a surrogate, above 10FFFF) gives U+FFFD. }
  THTMLEncoding = class(TNetEncoding)
  protected
    function DoEncode(const Input: string): string; overload; override;
    function DoDecode(const Input: string): string; overload; override;
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
  siURL = 3;
  siHTML = 4;

var
  SharedInstances: array[siBase64..siHTML] of Pointer;

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
    siBase64URL: Mine := TBase64URLEncoding.Create;
    siURL: Mine := TURLEncoding.Create;
  else
    Mine := THTMLEncoding.Create;
  end;
  if AtomicCmpExchange(SharedInstances[Index], Pointer(Mine), nil) <> nil then
    Mine.Free;
  Result := TNetEncoding(SharedInstances[Index]);
end;

procedure FreeSharedInstances;
var
  I: Integer;
begin
  for I := siBase64 to siHTML do
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

class function TNetEncoding.GetURL: TURLEncoding;
begin
  Result := TURLEncoding(SharedInstance(siURL));
end;

class function TNetEncoding.GetHTML: TNetEncoding;
begin
  Result := SharedInstance(siHTML);
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

{ ---------------------------------------------------------------- URL percent-encoding (RFC 3986 section 2) }

const
  { RFC 3986 section 2.3: these never need an escape }
  Unreserved: TURLEncoding.TUnsafeChars = [Ord('A')..Ord('Z'), Ord('a')..Ord('z'), Ord('0')..Ord('9'),
    Ord('-'), Ord('.'), Ord('_'), Ord('~')];
  { RFC 3986 section 2.2 }
  SubDelims: TURLEncoding.TUnsafeChars = [Ord('!'), Ord('$'), Ord('&'), Ord(''''), Ord('('), Ord(')'), Ord('*'),
    Ord('+'), Ord(','), Ord(';'), Ord('=')];
  { what a form field keeps (the form percent-encode set of the URL Standard): "~" is not among them }
  FormKept: TURLEncoding.TUnsafeChars = [Ord('A')..Ord('Z'), Ord('a')..Ord('z'), Ord('0')..Ord('9'),
    Ord('*'), Ord('-'), Ord('.'), Ord('_')];
  { the digits 0..9 and A..F as character codes }
  UpperHex: array[0..15] of Byte = (48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 65, 66, 67, 68, 69, 70);

{ the value of a hexadecimal digit of either case, or -1 }
function HexValue(C: Char): Integer;
begin
  if (C >= '0') and (C <= '9') then
    Result := Ord(C) - Ord('0')
  else if (C >= 'A') and (C <= 'F') then
    Result := Ord(C) - Ord('A') + 10
  else if (C >= 'a') and (C <= 'f') then
    Result := Ord(C) - Ord('a') + 10
  else
    Result := -1;
end;

function OrUtf8(AEncoding: TEncoding): TEncoding;
begin
  Result := AEncoding;
  if Result = nil then
    Result := TEncoding.UTF8;
end;

function TURLEncoding.Encode(const AInput: string; const ASet: TUnsafeChars; const AOptions: TEncodeOptions;
  AEncoding: TEncoding): string;
var
  Enc: TEncoding;
  Sink: TSink;
  I, K, N, Code, Units: Integer;
  Plain: Boolean;
  Bytes: TBytes;
begin
  Enc := OrUtf8(AEncoding);
  N := Length(AInput);
  SinkOpen(Sink, nil, N + N div 2);
  I := 1;
  while I <= N do
  begin
    Code := Ord(AInput[I]);
    Units := 1;
    if (Code = 32) and (SpacesAsPlus in AOptions) then
      SinkPut(Sink, Ord('+'))
    else
    begin
      if Code = Ord('%') then
        Plain := not ((EncodePercent in AOptions) or (Code in ASet))
      else
        Plain := (Code >= 32) and (Code <= 127) and not (Code in ASet);
      if Plain then
        SinkPut(Sink, Byte(Code))
      else
      begin
        { the character is turned into bytes first and each byte is written as an escape; the two halves of a
          surrogate pair are one character }
        if (Code >= $D800) and (Code <= $DBFF) and (I < N) and (AInput[I + 1] >= #$DC00) and
          (AInput[I + 1] <= #$DFFF) then
          Units := 2;
        Bytes := Enc.GetBytes(Copy(AInput, I, Units));
        for K := 0 to Length(Bytes) - 1 do
        begin
          SinkPut(Sink, Ord('%'));
          SinkPut(Sink, UpperHex[Bytes[K] shr 4]);
          SinkPut(Sink, UpperHex[Bytes[K] and 15]);
        end;
      end;
    end;
    I := I + Units;
  end;
  Result := BytesAsText(SinkBytes(Sink));
end;

function TURLEncoding.Decode(const AValue: string; const AOptions: TDecodeOptions; AEncoding: TEncoding): string;
var
  Enc: TEncoding;
  Sink: TSink;
  I, J, K, N, High4, Low4: Integer;
  Plus: Boolean;
  Bytes: TBytes;
begin
  Enc := OrUtf8(AEncoding);
  Plus := PlusAsSpaces in AOptions;
  N := Length(AValue);
  SinkOpen(Sink, nil, N);
  I := 1;
  while I <= N do
    if AValue[I] = '%' then
    begin
      High4 := -1;
      Low4 := -1;
      if I + 2 <= N then
      begin
        High4 := HexValue(AValue[I + 1]);
        Low4 := HexValue(AValue[I + 2]);
      end;
      if (High4 < 0) or (Low4 < 0) then
        raise EConvertError.CreateFmt(SUrlInvalidEscape, [I]);
      SinkPut(Sink, Byte(High4 * 16 + Low4));
      I := I + 3;
    end
    else if Plus and (AValue[I] = '+') then
    begin
      SinkPut(Sink, 32);
      Inc(I);
    end
    else
    begin
      { the characters up to the next escape or "+" stand for themselves: their bytes in the encoding }
      J := I + 1;
      while (J <= N) and (AValue[J] <> '%') and not (Plus and (AValue[J] = '+')) do
        Inc(J);
      Bytes := Enc.GetBytes(Copy(AValue, I, J - I));
      for K := 0 to Length(Bytes) - 1 do
        SinkPut(Sink, Bytes[K]);
      I := J;
    end;
  Result := Enc.GetString(SinkBytes(Sink));
end;

function TURLEncoding.EncodePath(const APath: string; const AExtraUnsafeChars: TUnsafeChars): string;
begin
  Result := Encode(APath, [32..127] - (Unreserved + SubDelims + [Ord(':'), Ord('@'), Ord('/')]) + AExtraUnsafeChars,
    []);
end;

function TURLEncoding.EncodeAuth(const AAuth: string; const AExtraUnsafeChars: TUnsafeChars): string;
begin
  Result := Encode(AAuth, [32..127] - (Unreserved + SubDelims) + AExtraUnsafeChars, []);
end;

function TURLEncoding.EncodeQuery(const AQuery: string; const AExtraUnsafeChars: TUnsafeChars): string;
begin
  Result := Encode(AQuery, [32..127] - Unreserved + AExtraUnsafeChars, []);
end;

function TURLEncoding.EncodeForm(const AInput: string; const AExtraUnsafeChars: TUnsafeChars;
  AEncoding: TEncoding): string;
begin
  Result := Encode(AInput, [32..127] - FormKept + AExtraUnsafeChars, [SpacesAsPlus], AEncoding);
end;

function TURLEncoding.URLDecode(const AValue: string): string;
begin
  Result := Decode(AValue, [], nil);
end;

function TURLEncoding.FormDecode(const AValue: string; AEncoding: TEncoding): string;
begin
  Result := Decode(AValue, [PlusAsSpaces], AEncoding);
end;

function TURLEncoding.DoEncode(const Input: string): string;
begin
  Result := EncodeQuery(Input);
end;

function TURLEncoding.DoDecode(const Input: string): string;
begin
  Result := URLDecode(Input);
end;

{ ---------------------------------------------------------------- HTML character escaping }

type
  TEntity = record
    Name: string;
    Code: Word;
  end;

const
  EntityCount = 253;
  MaxEntityName = 8;
  { The names Decode knows: the 252 character entity references of HTML 4.01 section 24 and "apos". They stand in
    ordinal order (capital letters first) because FindEntity searches by halving. The code points are those of the
    HTML Living Standard, which has "lang" and "rang" at U+27E8 and U+27E9. }
  Entities: array[0..EntityCount - 1] of TEntity = (
    (Name: 'AElig'; Code: 198), (Name: 'Aacute'; Code: 193), (Name: 'Acirc'; Code: 194), (Name: 'Agrave'; Code: 192),
    (Name: 'Alpha'; Code: 913), (Name: 'Aring'; Code: 197), (Name: 'Atilde'; Code: 195), (Name: 'Auml'; Code: 196),
    (Name: 'Beta'; Code: 914), (Name: 'Ccedil'; Code: 199), (Name: 'Chi'; Code: 935), (Name: 'Dagger'; Code: 8225),
    (Name: 'Delta'; Code: 916), (Name: 'ETH'; Code: 208), (Name: 'Eacute'; Code: 201), (Name: 'Ecirc'; Code: 202),
    (Name: 'Egrave'; Code: 200), (Name: 'Epsilon'; Code: 917), (Name: 'Eta'; Code: 919), (Name: 'Euml'; Code: 203),
    (Name: 'Gamma'; Code: 915), (Name: 'Iacute'; Code: 205), (Name: 'Icirc'; Code: 206), (Name: 'Igrave'; Code: 204),
    (Name: 'Iota'; Code: 921), (Name: 'Iuml'; Code: 207), (Name: 'Kappa'; Code: 922), (Name: 'Lambda'; Code: 923),
    (Name: 'Mu'; Code: 924), (Name: 'Ntilde'; Code: 209), (Name: 'Nu'; Code: 925), (Name: 'OElig'; Code: 338),
    (Name: 'Oacute'; Code: 211), (Name: 'Ocirc'; Code: 212), (Name: 'Ograve'; Code: 210), (Name: 'Omega'; Code: 937),
    (Name: 'Omicron'; Code: 927), (Name: 'Oslash'; Code: 216), (Name: 'Otilde'; Code: 213), (Name: 'Ouml'; Code: 214),
    (Name: 'Phi'; Code: 934), (Name: 'Pi'; Code: 928), (Name: 'Prime'; Code: 8243), (Name: 'Psi'; Code: 936),
    (Name: 'Rho'; Code: 929), (Name: 'Scaron'; Code: 352), (Name: 'Sigma'; Code: 931), (Name: 'THORN'; Code: 222),
    (Name: 'Tau'; Code: 932), (Name: 'Theta'; Code: 920), (Name: 'Uacute'; Code: 218), (Name: 'Ucirc'; Code: 219),
    (Name: 'Ugrave'; Code: 217), (Name: 'Upsilon'; Code: 933), (Name: 'Uuml'; Code: 220), (Name: 'Xi'; Code: 926),
    (Name: 'Yacute'; Code: 221), (Name: 'Yuml'; Code: 376), (Name: 'Zeta'; Code: 918), (Name: 'aacute'; Code: 225),
    (Name: 'acirc'; Code: 226), (Name: 'acute'; Code: 180), (Name: 'aelig'; Code: 230), (Name: 'agrave'; Code: 224),
    (Name: 'alefsym'; Code: 8501), (Name: 'alpha'; Code: 945), (Name: 'amp'; Code: 38), (Name: 'and'; Code: 8743),
    (Name: 'ang'; Code: 8736), (Name: 'apos'; Code: 39), (Name: 'aring'; Code: 229), (Name: 'asymp'; Code: 8776),
    (Name: 'atilde'; Code: 227), (Name: 'auml'; Code: 228), (Name: 'bdquo'; Code: 8222), (Name: 'beta'; Code: 946),
    (Name: 'brvbar'; Code: 166), (Name: 'bull'; Code: 8226), (Name: 'cap'; Code: 8745), (Name: 'ccedil'; Code: 231),
    (Name: 'cedil'; Code: 184), (Name: 'cent'; Code: 162), (Name: 'chi'; Code: 967), (Name: 'circ'; Code: 710),
    (Name: 'clubs'; Code: 9827), (Name: 'cong'; Code: 8773), (Name: 'copy'; Code: 169), (Name: 'crarr'; Code: 8629),
    (Name: 'cup'; Code: 8746), (Name: 'curren'; Code: 164), (Name: 'dArr'; Code: 8659), (Name: 'dagger'; Code: 8224),
    (Name: 'darr'; Code: 8595), (Name: 'deg'; Code: 176), (Name: 'delta'; Code: 948), (Name: 'diams'; Code: 9830),
    (Name: 'divide'; Code: 247), (Name: 'eacute'; Code: 233), (Name: 'ecirc'; Code: 234), (Name: 'egrave'; Code: 232),
    (Name: 'empty'; Code: 8709), (Name: 'emsp'; Code: 8195), (Name: 'ensp'; Code: 8194), (Name: 'epsilon'; Code: 949),
    (Name: 'equiv'; Code: 8801), (Name: 'eta'; Code: 951), (Name: 'eth'; Code: 240), (Name: 'euml'; Code: 235),
    (Name: 'euro'; Code: 8364), (Name: 'exist'; Code: 8707), (Name: 'fnof'; Code: 402), (Name: 'forall'; Code: 8704),
    (Name: 'frac12'; Code: 189), (Name: 'frac14'; Code: 188), (Name: 'frac34'; Code: 190),
    (Name: 'frasl'; Code: 8260), (Name: 'gamma'; Code: 947), (Name: 'ge'; Code: 8805), (Name: 'gt'; Code: 62),
    (Name: 'hArr'; Code: 8660), (Name: 'harr'; Code: 8596), (Name: 'hearts'; Code: 9829),
    (Name: 'hellip'; Code: 8230), (Name: 'iacute'; Code: 237), (Name: 'icirc'; Code: 238), (Name: 'iexcl'; Code: 161),
    (Name: 'igrave'; Code: 236), (Name: 'image'; Code: 8465), (Name: 'infin'; Code: 8734), (Name: 'int'; Code: 8747),
    (Name: 'iota'; Code: 953), (Name: 'iquest'; Code: 191), (Name: 'isin'; Code: 8712), (Name: 'iuml'; Code: 239),
    (Name: 'kappa'; Code: 954), (Name: 'lArr'; Code: 8656), (Name: 'lambda'; Code: 955), (Name: 'lang'; Code: 10216),
    (Name: 'laquo'; Code: 171), (Name: 'larr'; Code: 8592), (Name: 'lceil'; Code: 8968), (Name: 'ldquo'; Code: 8220),
    (Name: 'le'; Code: 8804), (Name: 'lfloor'; Code: 8970), (Name: 'lowast'; Code: 8727), (Name: 'loz'; Code: 9674),
    (Name: 'lrm'; Code: 8206), (Name: 'lsaquo'; Code: 8249), (Name: 'lsquo'; Code: 8216), (Name: 'lt'; Code: 60),
    (Name: 'macr'; Code: 175), (Name: 'mdash'; Code: 8212), (Name: 'micro'; Code: 181), (Name: 'middot'; Code: 183),
    (Name: 'minus'; Code: 8722), (Name: 'mu'; Code: 956), (Name: 'nabla'; Code: 8711), (Name: 'nbsp'; Code: 160),
    (Name: 'ndash'; Code: 8211), (Name: 'ne'; Code: 8800), (Name: 'ni'; Code: 8715), (Name: 'not'; Code: 172),
    (Name: 'notin'; Code: 8713), (Name: 'nsub'; Code: 8836), (Name: 'ntilde'; Code: 241), (Name: 'nu'; Code: 957),
    (Name: 'oacute'; Code: 243), (Name: 'ocirc'; Code: 244), (Name: 'oelig'; Code: 339), (Name: 'ograve'; Code: 242),
    (Name: 'oline'; Code: 8254), (Name: 'omega'; Code: 969), (Name: 'omicron'; Code: 959),
    (Name: 'oplus'; Code: 8853), (Name: 'or'; Code: 8744), (Name: 'ordf'; Code: 170), (Name: 'ordm'; Code: 186),
    (Name: 'oslash'; Code: 248), (Name: 'otilde'; Code: 245), (Name: 'otimes'; Code: 8855), (Name: 'ouml'; Code: 246),
    (Name: 'para'; Code: 182), (Name: 'part'; Code: 8706), (Name: 'permil'; Code: 8240), (Name: 'perp'; Code: 8869),
    (Name: 'phi'; Code: 966), (Name: 'pi'; Code: 960), (Name: 'piv'; Code: 982), (Name: 'plusmn'; Code: 177),
    (Name: 'pound'; Code: 163), (Name: 'prime'; Code: 8242), (Name: 'prod'; Code: 8719), (Name: 'prop'; Code: 8733),
    (Name: 'psi'; Code: 968), (Name: 'quot'; Code: 34), (Name: 'rArr'; Code: 8658), (Name: 'radic'; Code: 8730),
    (Name: 'rang'; Code: 10217), (Name: 'raquo'; Code: 187), (Name: 'rarr'; Code: 8594), (Name: 'rceil'; Code: 8969),
    (Name: 'rdquo'; Code: 8221), (Name: 'real'; Code: 8476), (Name: 'reg'; Code: 174), (Name: 'rfloor'; Code: 8971),
    (Name: 'rho'; Code: 961), (Name: 'rlm'; Code: 8207), (Name: 'rsaquo'; Code: 8250), (Name: 'rsquo'; Code: 8217),
    (Name: 'sbquo'; Code: 8218), (Name: 'scaron'; Code: 353), (Name: 'sdot'; Code: 8901), (Name: 'sect'; Code: 167),
    (Name: 'shy'; Code: 173), (Name: 'sigma'; Code: 963), (Name: 'sigmaf'; Code: 962), (Name: 'sim'; Code: 8764),
    (Name: 'spades'; Code: 9824), (Name: 'sub'; Code: 8834), (Name: 'sube'; Code: 8838), (Name: 'sum'; Code: 8721),
    (Name: 'sup'; Code: 8835), (Name: 'sup1'; Code: 185), (Name: 'sup2'; Code: 178), (Name: 'sup3'; Code: 179),
    (Name: 'supe'; Code: 8839), (Name: 'szlig'; Code: 223), (Name: 'tau'; Code: 964), (Name: 'there4'; Code: 8756),
    (Name: 'theta'; Code: 952), (Name: 'thetasym'; Code: 977), (Name: 'thinsp'; Code: 8201),
    (Name: 'thorn'; Code: 254), (Name: 'tilde'; Code: 732), (Name: 'times'; Code: 215), (Name: 'trade'; Code: 8482),
    (Name: 'uArr'; Code: 8657), (Name: 'uacute'; Code: 250), (Name: 'uarr'; Code: 8593), (Name: 'ucirc'; Code: 251),
    (Name: 'ugrave'; Code: 249), (Name: 'uml'; Code: 168), (Name: 'upsih'; Code: 978), (Name: 'upsilon'; Code: 965),
    (Name: 'uuml'; Code: 252), (Name: 'weierp'; Code: 8472), (Name: 'xi'; Code: 958), (Name: 'yacute'; Code: 253),
    (Name: 'yen'; Code: 165), (Name: 'yuml'; Code: 255), (Name: 'zeta'; Code: 950), (Name: 'zwj'; Code: 8205),
    (Name: 'zwnj'; Code: 8204));

  { A numeric reference to 80..9F means the character listed here (HTML Living Standard, numeric character
    reference end state); 0 marks the five values that stay as they are. }
  C1Characters: array[$80..$9F] of Word = (
    $20AC, 0, $201A, $0192, $201E, $2026, $2020, $2021, $02C6, $2030, $0160, $2039, $0152, 0, $017D, 0,
    0, $2018, $2019, $201C, $201D, $2022, $2013, $2014, $02DC, $2122, $0161, $203A, $0153, 0, $017E, $0178);

{ what Encode writes for C, or '' when C is copied }
function EscapeOf(C: Char): string;
begin
  case C of
    '&': Result := '&amp;';
    '<': Result := '&lt;';
    '>': Result := '&gt;';
    '"': Result := '&quot;';
    '''': Result := '&#39;';        { not "&apos;": HTML 4 does not define that name }
  else
    Result := '';
  end;
end;

{ the place of Name in Entities, or -1; upper and lower case differ }
function FindEntity(const Name: string): Integer;
var
  First, Last, Middle: Integer;
begin
  Result := -1;
  First := 0;
  Last := EntityCount - 1;
  while (Result < 0) and (First <= Last) do
  begin
    Middle := (First + Last) div 2;
    if Entities[Middle].Name = Name then
      Result := Middle
    else if Entities[Middle].Name < Name then
      First := Middle + 1
    else
      Last := Middle - 1;
  end;
end;

{ the code point a numeric reference with this value stands for }
function NumericCodePoint(Value: Cardinal): Integer;
begin
  if (Value = 0) or (Value > $10FFFF) or ((Value >= $D800) and (Value <= $DFFF)) then
    Result := $FFFD
  else if (Value >= $80) and (Value <= $9F) and (C1Characters[Integer(Value)] <> 0) then
    Result := C1Characters[Integer(Value)]
  else
    Result := Integer(Value);
end;

{ S[At] is "&". When a complete reference starts there, the result is its code point and Next the index behind its
  ";"; otherwise the result is -1. }
function ReadReference(const S: string; At: Integer; var Next: Integer): Integer;
var
  N, I, First, Base, Digit, Found: Integer;
  Value: Cardinal;
begin
  Result := -1;
  N := Length(S);
  I := At + 1;
  if (I <= N) and (S[I] = '#') then
  begin
    Inc(I);
    Base := 10;
    if (I <= N) and ((S[I] = 'x') or (S[I] = 'X')) then
    begin
      Base := 16;
      Inc(I);
    end;
    First := I;
    Value := 0;
    while I <= N do
    begin
      Digit := HexValue(S[I]);
      if (Digit < 0) or (Digit >= Base) then
        Break;
      { any number of digits may follow; a value above the last code point stays where it is }
      if Value <= $10FFFF then
        Value := Value * Cardinal(Base) + Cardinal(Digit);
      Inc(I);
    end;
    if (I > First) and (I <= N) and (S[I] = ';') then
    begin
      Result := NumericCodePoint(Value);
      Next := I + 1;
    end;
  end
  else
  begin
    { a name consists of letters and digits and ends directly at the ";" }
    while (I <= N) and (I - At <= MaxEntityName) and (((S[I] >= 'a') and (S[I] <= 'z')) or
      ((S[I] >= 'A') and (S[I] <= 'Z')) or ((S[I] >= '0') and (S[I] <= '9'))) do
      Inc(I);
    if (I > At + 1) and (I <= N) and (S[I] = ';') then
    begin
      Found := FindEntity(Copy(S, At + 1, I - At - 1));
      if Found >= 0 then
      begin
        Result := Entities[Found].Code;
        Next := I + 1;
      end;
    end;
  end;
end;

function THTMLEncoding.DoEncode(const Input: string): string;
var
  I, K, Len, Extra: Integer;
  Escape: string;
begin
  Extra := 0;
  for I := 1 to Length(Input) do
  begin
    Escape := EscapeOf(Input[I]);
    if Escape <> '' then
      Extra := Extra + Length(Escape) - 1;
  end;
  Result := Input;
  if Extra = 0 then
    Exit;
  SetLength(Result, Length(Input) + Extra);
  Len := 0;
  for I := 1 to Length(Input) do
  begin
    Escape := EscapeOf(Input[I]);
    if Escape = '' then
    begin
      Inc(Len);
      Result[Len] := Input[I];
    end
    else
      for K := 1 to Length(Escape) do
      begin
        Inc(Len);
        Result[Len] := Escape[K];
      end;
  end;
end;

{ One pass from the left; what a reference was replaced by is not looked at again. A "&" that does not start a
  complete reference is copied. }
function THTMLEncoding.DoDecode(const Input: string): string;
var
  N, I, Len, Next, Code: Integer;
begin
  N := Length(Input);
  SetLength(Result, N);        { no reference is shorter than the text it stands for }
  Len := 0;
  Next := 0;
  I := 1;
  while I <= N do
  begin
    Code := -1;
    if Input[I] = '&' then
      Code := ReadReference(Input, I, Next);
    if Code < 0 then
    begin
      Inc(Len);
      Result[Len] := Input[I];
      Inc(I);
    end
    else
    begin
      if Code > $FFFF then
      begin
        Inc(Len);
        Result[Len] := Char($D800 + (Code - $10000) shr 10);
        Code := $DC00 + (Code - $10000) and $3FF;
      end;
      Inc(Len);
      Result[Len] := Char(Code);
      I := Next;
    end;
  end;
  SetLength(Result, Len);
end;

initialization

finalization
  FreeSharedInstances;
end.
