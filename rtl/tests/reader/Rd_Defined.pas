// phase: 4
// covers: RTL-457, RTL-505, RTL-496
program Rd_Defined;
{ Defined properties (classes.md RTL-496 step 2, RTL-505): a name that is not published is offered to
  DefineProperties; the matching DefineProperty read proc consumes one value with the Read* methods (NextValue
  kinds, lists through ReadListBegin/EndOfList/ReadListEnd, sets, identifiers); DefineBinaryProperty receives the
  decoded brace block as a stream at position 0; sub-objects define properties too; DesignLeft/DesignTop of
  TComponent fill DesignInfo (RTL-457); a token that does not fit the Read* call is F0103; bad binary text F0001;
  an unmatched name stays F0102. }
uses
  SysUtils, TypInfo, Classes, TestKit;

type
  TABC = (a, b, c);
  TABCs = set of TABC;

  TSecretHolder = class(TPersistent)
  public
    Secret: Integer;
  protected
    procedure DefineProperties(Filer: TFiler); override;
    procedure ReadSecret(Reader: TReader);
  end;

  TDefComp = class(TComponent)
  private
    FSub: TSecretHolder;
  public
    Log: string;
    Blob: string;
    BlobPos: Int64;
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  protected
    procedure DefineProperties(Filer: TFiler); override;
    procedure Probe(Reader: TReader);
    procedure ReadLines(Reader: TReader);
    procedure ReadNum(Reader: TReader);
    procedure ReadMisc(Reader: TReader);
    procedure ReadBlob(Stream: TStream);
  published
    property Sub: TSecretHolder read FSub;
  end;

procedure TSecretHolder.DefineProperties(Filer: TFiler);
begin
  inherited DefineProperties(Filer);
  Filer.DefineProperty('Secret', ReadSecret, nil, True);
end;

procedure TSecretHolder.ReadSecret(Reader: TReader);
begin
  Secret := Reader.ReadInteger;
end;

constructor TDefComp.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FSub := TSecretHolder.Create;
end;

destructor TDefComp.Destroy;
begin
  FSub.Free;
  inherited Destroy;
end;

procedure TDefComp.DefineProperties(Filer: TFiler);
var
  I: Integer;
begin
  inherited DefineProperties(Filer);
  for I := 1 to 15 do
    Filer.DefineProperty('K' + IntToStr(I), Probe, nil, True);
  Filer.DefineProperty('Lines', ReadLines, nil, True);
  Filer.DefineProperty('Num', ReadNum, nil, True);
  Filer.DefineProperty('Misc', ReadMisc, nil, True);
  Filer.DefineBinaryProperty('Data', ReadBlob, nil, True);
end;

procedure TDefComp.Probe(Reader: TReader);
begin
  Log := Log + GetEnumName(TypeInfo(TValueType), Ord(Reader.NextValue)) + ' ';
  Reader.SkipValue;
end;

procedure TDefComp.ReadLines(Reader: TReader);
begin
  Reader.ReadListBegin;
  while not Reader.EndOfList do
    Log := Log + '[' + Reader.ReadString + ']';
  Reader.ReadListEnd;
end;

procedure TDefComp.ReadNum(Reader: TReader);
begin
  Log := Log + 'num ' + IntToStr(Reader.ReadInteger) + ';';
end;

{ Misc = (... ) holding one value of each Read* kind, in a fixed order }
procedure TDefComp.ReadMisc(Reader: TReader);
begin
  Reader.ReadListBegin;
  Log := Log + 'int64 ' + IntToStr(Reader.ReadInt64) + ';';
  Log := Log + 'float ' + FloatToStr(Reader.ReadFloat) + ';';
  Log := Log + 'bool ' + BoolToStr(Reader.ReadBoolean, True) + ';';
  Log := Log + 'ident ' + Reader.ReadIdent + ';';
  Log := Log + 'char ' + Reader.ReadChar + ';';
  Log := Log + 'set ' + IntToStr(Reader.ReadSet(TypeInfo(TABCs))) + ';';
  Reader.ReadListEnd;
end;

procedure TDefComp.ReadBlob(Stream: TStream);
var
  B: Byte;
begin
  BlobPos := Stream.Position;
  Blob := '';
  while Stream.Read(B, 1) = 1 do
    Blob := Blob + IntToHex(B, 2);
end;

function Err(const Body: string): string;
var
  Q: TDefComp;
begin
  Result := '';
  Q := TDefComp.Create(nil);
  try
    ReadComponentFromText('object D: TDefComp'#10 + Body + 'end'#10, Q, 'D.form');
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
  Q.Free;
end;

var
  D: TDefComp;
begin
  D := TDefComp.Create(nil);

  Section('NextValue kinds (section 10.3)');
  ReadComponentFromText(
    'object D: TDefComp'#10 +
    '  K1 = -5'#10 +
    '  K2 = 300'#10 +
    '  K3 = 70000'#10 +
    '  K4 = 5000000000'#10 +
    '  K5 = $FF'#10 +
    '  K6 = 1.5'#10 +
    '  K7 = ''s'' +'#10'    ''t'''#10 +
    '  K8 = True'#10 +
    '  K9 = false'#10 +
    '  K10 = nil'#10 +
    '  K11 = Some.Ident'#10 +
    '  K12 = [a, b]'#10 +
    '  K13 = ('#10'    ''x'''#10'  )'#10 +
    '  K14 = {'#10'    00FF'#10'  }'#10 +
    '  K15 = <'#10'    item'#10'      Data = {'#10'        AB'#10'      }'#10'    end'#10'  >'#10 +
    'end'#10, D, 'D.form');
  CheckEqS('vaInt8 vaInt16 vaInt32 vaInt64 vaInt32 vaExtended vaString vaTrue vaFalse vaNil vaIdent vaSet vaList ' +
    'vaBinary vaCollection ', D.Log, 'every token kind, SkipValue over each');

  Section('Read* methods (RTL-505)');
  D.Log := '';
  ReadComponentFromText(
    'object D: TDefComp'#10 +
    '  Lines = ('#10'    ''one'''#10'    ''two'''#10'  )'#10 +
    '  Num = $FFFFFFFF'#10 +
    '  num = 42'#10 +
    '  Misc = ('#10'    5000000000 2.5 True Foo.Bar ''x'' [a, c]'#10'  )'#10 +
    '  Data = {'#10'    0a1B'#10'    ff'#10'  }'#10 +
    '  Sub.Secret = 9'#10 +
    '  DesignLeft = 16'#10 +
    '  DesignTop = 300'#10 +
    'end'#10, D, 'D.form');
  CheckEqS('[one][two]num -1;num 42;int64 5000000000;float 2.5;bool True;ident Foo.Bar;char x;set 5;', D.Log,
    'list, integers, every Read* kind');
  CheckEqS('0A1BFF', D.Blob, 'binary decoded across lines and in any case');
  CheckEqI(0, D.BlobPos, 'the stream is at position 0');
  CheckEqI(9, D.Sub.Secret, 'a defined property of a sub-object');
  CheckEqI(16, D.DesignInfo and $FFFF, 'DesignLeft in the low word (RTL-457)');
  CheckEqI(300, D.DesignInfo shr 16, 'DesignTop in the high word');

  Section('errors');
  CheckEqS('EReadError: D.form(2,9): error F0103: Invalid value for property Num', Err('  Num = ''x'''#10),
    'string for ReadInteger');
  CheckEqS('EReadError: D.form(2,9): error F0103: Invalid value for property Num', Err('  Num = 5000000000'#10),
    'ReadInteger out of range');
  CheckEqS('EReadError: D.form(2,10): error F0103: Invalid value for property Data', Err('  Data = 5'#10),
    'number for a binary property');
  CheckEqS('EReadError: D.form(3,7): error F0001: Syntax error: invalid character',
    Err('  Data = {'#10'    0AG1'#10'  }'#10), 'bad hex digit');
  CheckEqS('EReadError: D.form(4,3): error F0001: Syntax error: invalid character',
    Err('  Data = {'#10'    0A1'#10'  }'#10), 'odd number of hex digits');
  CheckEqS('EReadError: D.form(2,3): error F0102: Property Nope does not exist on TDefComp', Err('  Nope = 1'#10),
    'unmatched name');
  CheckEqS('EReadError: D.form(2,3): error F0102: Property Sub.Nope does not exist on TDefComp', Err('  Sub.Nope = 1'#10),
    'unmatched name on a sub-object');
  D.Free;
  Done;
end.
