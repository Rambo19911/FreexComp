// phase: 4
// covers: RTL-485, RTL-486, RTL-487, RTL-488, RTL-489, RTL-490, RTL-491
program Rd_Lexer;
{ The form text lexer through ReadComponentFromText (classes.md section 10.4): UTF-8 with an optional BOM, invalid
  UTF-8 is F0001 (RTL-485); LF and CRLF (RTL-486); blanks, tabs, any indentation, blank lines, trailing
  whitespace (RTL-487); keywords case-insensitive (RTL-488); strings: doubled quotes, #n and #$h code units,
  adjacent parts, "+" continuation at the end of a line (RTL-489); integers within Int64, $ with 1-16 digits, up to
  8 digits a 32-bit pattern (RTL-490); every unexpected character is F0001 with its 1-based line and UTF-16 column
  (RTL-491, RTL-500). }
uses
  SysUtils, Classes, TestKit;

type
  TLex = class(TComponent)
  private
    FS: string;
    FI: Integer;
    FI64: Int64;
    FF: Double;
    FU: Cardinal;
  published
    property S: string read FS write FS;
    property I: Integer read FI write FI;
    property I64: Int64 read FI64 write FI64;
    property F: Double read FF write FF;
    property U: Cardinal read FU write FU;
  end;

const
  Head = 'object Lex1: TLex'#10;

var
  X: TLex;

{ reads Text into a fresh X }
procedure Load(const Text: string);
begin
  X.Free;
  X := TLex.Create(nil);
  ReadComponentFromText(Text, X, 'Lex.form');
end;

{ the property line P inside a minimal form }
procedure LoadProp(const P: string);
begin
  Load(Head + '  ' + P + #10'end'#10);
end;

{ the class and message of the exception reading Text raises }
function Err(const Text: string): string;
begin
  Result := '';
  try
    Load(Text);
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

function PropErr(const P: string): string;
begin
  Result := Err(Head + '  ' + P + #10'end'#10);
end;

{ reads raw bytes: the UTF-8 of Prefix, the bytes B, the UTF-8 of Suffix }
function ErrBytes(const Prefix: string; const B: array of Byte; const Suffix: string): string;
var
  Bytes, P, Q: TBytes;
  N, K: Integer;
  Stream: TBytesStream;
  R: TReader;
begin
  P := TEncoding.UTF8.GetBytes(Prefix);
  Q := TEncoding.UTF8.GetBytes(Suffix);
  SetLength(Bytes, Length(P) + Length(B) + Length(Q));
  N := 0;
  for K := 0 to High(P) do
  begin
    Bytes[N] := P[K];
    N := N + 1;
  end;
  for K := 0 to High(B) do
  begin
    Bytes[N] := B[K];
    N := N + 1;
  end;
  for K := 0 to High(Q) do
  begin
    Bytes[N] := Q[K];
    N := N + 1;
  end;
  Result := '';
  X.Free;
  X := TLex.Create(nil);
  Stream := TBytesStream.Create(Bytes);
  R := TReader.Create(Stream, 4096);
  try
    R.SourceName := 'Raw.form';
    try
      R.ReadRootComponent(X);
    except
      on E: Exception do
        Result := E.ClassName + ': ' + E.Message;
    end;
  finally
    R.Free;
    Stream.Free;
  end;
end;

function Syn(Line, Col: Integer; const Detail: string): string;
begin
  Result := 'EReadError: Lex.form(' + IntToStr(Line) + ',' + IntToStr(Col) + '): error F0001: Syntax error: ' + Detail;
end;

begin
  Section('tolerances (RTL-485 ... RTL-488)');
  Load(#$FEFF + #10#13#10'OBJECT Lex1: TLex'#13#10#9'  s   =   ''x'' '#9#13#10#13#10'   I=5'#10'End'#13#10#10);
  CheckEqS('Lex1', X.Name, 'BOM, CRLF, leading blank lines, keyword case');
  CheckEqS('x', X.S, 'tabs, indentation, trailing whitespace');
  CheckEqI(5, X.I, 'no blanks around "="');
  Load('inherited Lex2: TSomethingElse'#10'end');
  CheckEqS('Lex2', X.Name, 'inherited root, root type not validated, no final line end');
  Check(not (csLoading in X.ComponentState), 'csLoading cleared by Loaded');

  Section('strings (RTL-489)');
  LoadProp('S = ''it''''s''');
  CheckEqS('it''s', X.S, 'doubled quote');
  LoadProp('S = ''a''#13#10''b''');
  CheckEqS('a'#13#10'b', X.S, 'control codes between parts');
  LoadProp('S = #$41#66''c''#$00e9');
  CheckEqS('ABc'#$00E9, X.S, 'decimal and hex code units');
  LoadProp('S = ''''');
  CheckEqS('', X.S, 'empty string');
  LoadProp('S = ''' + #$00E9#$20AC#$D83D#$DE00 + '''');
  CheckEqS(#$00E9#$20AC#$D83D#$DE00, X.S, 'non-ASCII literally');
  LoadProp('S = ''abc'' +'#10'      ''def''#9+  '#13#10#13#10'  #33');
  CheckEqS('abcdef'#9'!', X.S, '"+" continues on the next line');

  Section('numbers (RTL-490)');
  LoadProp('I = -5');
  CheckEqI(-5, X.I, 'negative');
  LoadProp('I = $ff');
  CheckEqI(255, X.I, 'hex, lower case');
  LoadProp('I = $FFFFFFFF');
  CheckEqI(-1, X.I, '32-bit pattern into Integer');
  LoadProp('I = $80000000');
  CheckEqI(-2147483647 - 1, X.I, 'lowest Integer pattern');
  LoadProp('U = $FFFFFFFF');
  CheckEqS('4294967295', IntToStr(X.U), '32-bit pattern into Cardinal');
  LoadProp('U = 4294967295');
  CheckEqS('4294967295', IntToStr(X.U), 'decimal Cardinal');
  LoadProp('I64 = 9223372036854775807');
  CheckEqS('9223372036854775807', IntToStr(X.I64), 'highest Int64');
  LoadProp('I64 = -9223372036854775808');
  CheckEqS('-9223372036854775808', IntToStr(X.I64), 'lowest Int64');
  LoadProp('I64 = $FFFFFFFFFFFFFFFF');
  CheckEqS('-1', IntToStr(X.I64), '16 hex digits');
  LoadProp('I64 = $FFFFFFFF');
  CheckEqS('4294967295', IntToStr(X.I64), 'short hex into Int64 is unsigned');
  LoadProp('F = 1.5');
  CheckEqF(1.5, X.F, 0, 'float');
  LoadProp('F = -2.25E+3');
  CheckEqF(-2250, X.F, 0, 'exponent');
  LoadProp('F = 1.0e-5');
  CheckEqF(1.0e-5, X.F, 0, 'small exponent');
  LoadProp('F = 7');
  CheckEqF(7, X.F, 0, 'integer into a float');

  Section('syntax errors (RTL-491, RTL-500)');
  CheckEqS(Syn(2, 7, 'invalid character'), PropErr('S = @'), 'invalid character');
  CheckEqS(Syn(2, 7, 'unterminated string'), PropErr('S = ''abc'), 'unterminated string');
  CheckEqS(Syn(2, 5, 'expected "=" but found "abc"'), PropErr('S abc'), 'missing "="');
  CheckEqS(Syn(2, 6, 'expected value but found end of line'), PropErr('S ='), 'missing value');
  CheckEqS(Syn(3, 1, 'unexpected end of file'), Err(Head + '  S = ''1'''#10), 'missing end');
  CheckEqS(Syn(2, 9, 'integer out of range'), PropErr('I64 = 9223372036854775808'), 'decimal out of range');
  CheckEqS(Syn(2, 9, 'integer out of range'), PropErr('I64 = -9223372036854775809'), 'negative out of range');
  CheckEqS(Syn(2, 9, 'integer out of range'), PropErr('I64 = $1FFFFFFFFFFFFFFFF'), '17 hex digits');
  CheckEqS(Syn(2, 7, 'integer out of range'), PropErr('S = #65536'), 'code unit out of range');
  CheckEqS(Syn(2, 8, 'invalid character'), PropErr('S = #x'), '"#" without digits');
  CheckEqS(Syn(2, 11, 'invalid character'), PropErr('S = ''a'' + ''b'''), '"+" not at the end of a line');
  CheckEqS(Syn(2, 7, 'invalid character'), PropErr('I = +5'), 'a lone "+"');
  CheckEqS(Syn(2, 10, 'invalid character'), PropErr('S = ''a'''#13'I = 1'), 'a lone CR');
  CheckEqS(Syn(2, 9, 'expected end of line but found "b"'), PropErr('I = 1 b'), 'junk after the value');
  CheckEqS(Syn(2, 12, 'invalid character'), PropErr('S = ''' + #$D83D#$DE00 + ''' @'), 'columns count UTF-16 units');
  CheckEqS(Syn(3, 7, 'invalid character'), Err('object A: TLex'#13#10#13#10'  S = @'), 'CRLF line counting');
  CheckEqS(Syn(1, 10, 'expected ":" but found "TLex"'), Err('object A TLex'#10'end'), 'header');
  CheckEqS(Syn(1, 1, 'expected "object" but found "thing"'), Err('thing A: TLex'#10'end'), 'first keyword');
  CheckEqS(Syn(3, 1, 'expected end of file but found "x"'), Err('object A: TLex'#10'end'#10'x'#10), 'text after the root');
  Check(not (csLoading in X.ComponentState), 'csLoading cleared after an error');
  Check(not (csReading in X.ComponentState), 'csReading cleared after an error');
  CheckEqS(Syn(1, 1, 'unexpected end of file'), Err(''), 'empty text');

  Section('invalid UTF-8 (RTL-485)');
  CheckEqS('EReadError: Raw.form(2,8): error F0001: Syntax error: invalid UTF-8',
    ErrBytes(Head + '  S = ''', [$C3], '''' + #10'end'#10), 'truncated sequence');
  CheckEqS('EReadError: Raw.form(2,9): error F0001: Syntax error: invalid UTF-8',
    ErrBytes(Head + '  S = ''' + #$00E9, [$C0, $80], '''' + #10'end'#10), 'overlong form');
  CheckEqS('EReadError: Raw.form(1,1): error F0001: Syntax error: invalid UTF-8',
    ErrBytes('', [$ED, $A0, $80], ''), 'encoded surrogate');
  CheckEqS('EReadError: Raw.form(1,1): error F0001: Syntax error: invalid UTF-8',
    ErrBytes('', [$FF], ''), 'invalid lead byte');
  CheckEqS('', ErrBytes('', [$EF, $BB, $BF], 'object A: TLex'#10'end'#10), 'BOM skipped');
  X.Free;
  Done;
end.
