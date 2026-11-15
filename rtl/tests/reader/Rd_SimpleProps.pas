// phase: 4
// covers: RTL-480, RTL-495, RTL-496
program Rd_SimpleProps;
{ TReader skeleton (classes.md section 10.3/10.5): ReadComponentFromText reads the root header (name applied, type
  name not validated) and the simple property kinds of RTL-496 through RTTI: integer types with range checks and
  RegisterIntegerConsts identifiers, Int64, Char/AnsiChar from a one-unit string, enumerations, Boolean, sets,
  Single/Double/TDateTime/Currency from float or integer, string/AnsiString/ShortString, a class property set to nil;
  index and setter-method properties; inherited published properties (Tag); a nil instance is created from the
  header class (GetClass); out-of-range and wrong-kind values are F0103 at the value. }
uses
  SysUtils, Classes, TypInfo, TestKit;

type
  TColorX = -$7FFFFFFF - 1..$7FFFFFFF;
  TFruit = (frApple, frBanana, frCherry);
  TFruits = set of TFruit;
  TSmall = 0..200;
  TShort9 = string[9];

  TProps = class(TComponent)
  private
    FB: Byte;
    FSm: TSmall;
    FSh: ShortInt;
    FCol: TColorX;
    FCh: Char;
    FAc: AnsiChar;
    FFr: TFruit;
    FFrs: TFruits;
    FBool: Boolean;
    FSi: Single;
    FDt: TDateTime;
    FAStr: AnsiString;
    FSStr: TShort9;
    FI64: Int64;
    FObj: TPersistent;
    FParts: array[0..2] of Integer;
    FVal: Integer;
    FPrice: Currency;
    FPriceM: Currency;
    function GetPart(Index: Integer): Integer;
    procedure SetPart(Index: Integer; V: Integer);
    procedure SetVal(V: Integer);
    function GetPriceM: Currency;
    procedure SetPriceM(V: Currency);
  public
    Log: string;
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  published
    property B: Byte read FB write FB;
    property Sm: TSmall read FSm write FSm;
    property Sh: ShortInt read FSh write FSh;
    property Col: TColorX read FCol write FCol;
    property Ch: Char read FCh write FCh;
    property Ac: AnsiChar read FAc write FAc;
    property Fr: TFruit read FFr write FFr;
    property Frs: TFruits read FFrs write FFrs;
    property Bool: Boolean read FBool write FBool;
    property Si: Single read FSi write FSi;
    property Dt: TDateTime read FDt write FDt;
    property AStr: AnsiString read FAStr write FAStr;
    property SStr: TShort9 read FSStr write FSStr;
    property I64: Int64 read FI64 write FI64;
    property Obj: TPersistent read FObj write FObj;
    property Part0: Integer index 0 read GetPart write SetPart;
    property Part2: Integer index 2 read GetPart write SetPart;
    property Val: Integer read FVal write SetVal;
    property Price: Currency read FPrice write FPrice;
    property PriceM: Currency read GetPriceM write SetPriceM;
  end;

const
  ColorMap: array[0..1] of TIdentMapEntry = ((Value: $0000FF; Name: 'clRed'), (Value: $FFFFFF; Name: 'clWhite'));

function IdentToColor(const Ident: string; var Int: Integer): Boolean;
begin
  Result := IdentToInt(Ident, Int, ColorMap);
end;

function ColorToIdent(Int: Integer; var Ident: string): Boolean;
begin
  Result := IntToIdent(Int, Ident, ColorMap);
end;

constructor TProps.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FObj := TPersistent.Create;
end;

destructor TProps.Destroy;
begin
  FObj.Free;
  inherited Destroy;
end;

function TProps.GetPart(Index: Integer): Integer;
begin
  Result := FParts[Index];
end;

procedure TProps.SetPart(Index: Integer; V: Integer);
begin
  FParts[Index] := V;
  Log := Log + 'part' + IntToStr(Index) + '=' + IntToStr(V) + ';';
end;

procedure TProps.SetVal(V: Integer);
begin
  FVal := V;
  Log := Log + 'val=' + IntToStr(V) + ';';
end;

function TProps.GetPriceM: Currency;
begin
  Result := FPriceM;
end;

procedure TProps.SetPriceM(V: Currency);
begin
  FPriceM := V;
  Log := Log + 'pricem;';
end;

function Raw(const C: Currency): string;
begin
  Result := IntToStr(PInt64(@C)^);
end;

var
  P: TProps;
  Keep: TPersistent;
  C: TComponent;

function Err(const Line: string): string;
var
  Q: TProps;
begin
  Result := '';
  Q := TProps.Create(nil);
  try
    ReadComponentFromText('object Q: TProps'#10'  ' + Line + #10'end'#10, Q, 'P.form');
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
  Q.Free;
end;

function Bad(Col: Integer; const Prop: string): string;
begin
  Result := 'EReadError: P.form(2,' + IntToStr(Col) + '): error F0103: Invalid value for property ' + Prop;
end;

begin
  RegisterIntegerConsts(TypeInfo(TColorX), IdentToColor, ColorToIdent);
  P := TProps.Create(nil);
  Keep := P.Obj;

  Section('header and values (RTL-495, RTL-496)');
  C := ReadComponentFromText(
    'object Props1: TWhatever'#10 +
    '  B = 255'#10 +
    '  Sm = 200'#10 +
    '  Sh = -128'#10 +
    '  Col = clWhite'#10 +
    '  Ch = #$263A'#10 +
    '  Ac = ''z'''#10 +
    '  Fr = frCherry'#10 +
    '  Frs = [frApple, frCherry]'#10 +
    '  Bool = True'#10 +
    '  Si = 0.5'#10 +
    '  Dt = 45000.25'#10 +
    '  AStr = ''ansi'''#10 +
    '  SStr = ''short'''#10 +
    '  I64 = -40000000000'#10 +
    '  Obj = nil'#10 +
    '  Part2 = 22'#10 +
    '  Part0 = 10'#10 +
    '  Val = $10'#10 +
    '  Tag = 77'#10 +
    'end'#10, P, 'P.form');
  Check(C = P, 'ReadComponentFromText returns the instance');
  CheckEqS('Props1', P.Name, 'root name from the header');
  CheckEqI(255, P.B, 'Byte');
  CheckEqI(200, P.Sm, 'subrange');
  CheckEqI(-128, P.Sh, 'ShortInt');
  CheckEqI($FFFFFF, P.Col, 'RegisterIntegerConsts identifier');
  CheckEqI($263A, Ord(P.Ch), 'Char');
  CheckEqI(Ord('z'), Ord(P.Ac), 'AnsiChar');
  Check(P.Fr = frCherry, 'enumeration');
  Check(P.Frs = [frApple, frCherry], 'set');
  CheckEqB(True, P.Bool, 'Boolean');
  CheckEqF(0.5, P.Si, 0, 'Single');
  CheckEqF(45000.25, P.Dt, 0, 'TDateTime');
  CheckEqS('ansi', string(P.AStr), 'AnsiString');
  CheckEqS('short', string(P.SStr), 'ShortString');
  CheckEqS('-40000000000', IntToStr(P.I64), 'Int64');
  Check(P.Obj = nil, 'class property set to nil');
  CheckEqS('part2=22;part0=10;val=16;', P.Log, 'index and setter methods, in file order');
  CheckEqS('77', IntToStr(P.Tag), 'inherited published property');
  Check(P.ComponentState = [], 'csLoading and csReading cleared');
  P.Obj := Keep;

  Section('more values');
  ReadComponentFromText('object Props1: TProps'#10'  Col = $FF0000'#10'  Frs = []'#10'  Bool = false'#10 +
    '  Si = 3'#10'  Fr = FRBANANA'#10'end'#10, P, 'P.form');
  CheckEqI($FF0000, P.Col, 'colour as hex');
  Check(P.Frs = [], 'empty set');
  CheckEqB(False, P.Bool, 'Boolean is case-insensitive');
  CheckEqF(3, P.Si, 0, 'integer into Single');
  Check(P.Fr = frBanana, 'enumeration is case-insensitive');

  Section('Currency (FloatType 2, C-218)');
  P.Log := '';
  ReadComponentFromText('object Props1: TProps'#10'  Price = 12.3456'#10'  PriceM = 1'#10'end'#10, P, 'P.form');
  CheckEqS('123456', Raw(P.FPrice), 'Currency field reads exactly');
  CheckEqS('10000', Raw(P.FPriceM), 'integer into a Currency setter');
  CheckEqS('pricem;', P.Log, 'Currency setter method called');
  ReadComponentFromText('object Props1: TProps'#10'  Price = 1'#10'  PriceM = -0.0001'#10'end'#10, P, 'P.form');
  CheckEqS('10000', Raw(P.FPrice), 'integer into a Currency field');
  CheckEqS('-1', Raw(P.FPriceM), 'smallest negative Currency');
  CheckEqF(12.5, GetFloatProp(P, 'Price') + 11.5, 0, 'GetFloatProp of a Currency field');
  CheckEqF(-0.0001, GetFloatProp(P, 'PriceM'), 0, 'GetFloatProp of a Currency getter');
  CheckEqS(Bad(11, 'Price'), Err('Price = ''x'''), 'string for a Currency');

  Section('a nil instance');
  RegisterClass(TProps);
  C := ReadComponentFromText('object Made: TProps'#10'  B = 9'#10'end'#10, nil);
  Check(C is TProps, 'created from the header class');
  CheckEqS('Made', C.Name, 'named');
  CheckEqI(9, TProps(C).B, 'properties read');
  C.Free;

  Section('invalid values (F0103)');
  CheckEqS(Bad(7, 'B'), Err('B = 256'), 'Byte out of range');
  CheckEqS(Bad(8, 'Sm'), Err('Sm = -1'), 'subrange out of range');
  CheckEqS(Bad(7, 'B'), Err('B = ''1'''), 'string for an integer');
  CheckEqS(Bad(9, 'Col'), Err('Col = clNope'), 'unknown identifier');
  CheckEqS(Bad(7, 'B'), Err('B = clRed'), 'identifier without a map');
  CheckEqS(Bad(8, 'Ch'), Err('Ch = ''ab'''), 'two code units for a Char');
  CheckEqS(Bad(8, 'Fr'), Err('Fr = frKiwi'), 'unknown enumeration value');
  CheckEqS(Bad(8, 'Fr'), Err('Fr = 1'), 'number for an enumeration');
  CheckEqS(Bad(10, 'Frs'), Err('Frs = [frKiwi]'), 'unknown set element');
  CheckEqS(Bad(9, 'Frs'), Err('Frs = frApple'), 'set without brackets');
  CheckEqS(Bad(10, 'Bool'), Err('Bool = Maybe'), 'unknown Boolean');
  CheckEqS(Bad(8, 'Si'), Err('Si = ''x'''), 'string for a float');
  CheckEqS(Bad(10, 'AStr'), Err('AStr = 5'), 'number for a string');
  CheckEqS(Bad(9, 'I64'), Err('I64 = 1.5'), 'float for an Int64');
  CheckEqS(Bad(9, 'Obj'), Err('Obj = 5'), 'number for an object');
  P.Free;
  Done;
end.
