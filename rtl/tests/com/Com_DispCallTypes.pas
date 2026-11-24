// covers: RTL-1014
// phase: 8
program Com_DispCallTypes;
{ com-support.md RTL-1014 steps 1, 3 and 5: a hand-written IDispatch server (unit DispKit) writes down the argument
  records it receives. Every Automation type by value and by reference (type numbers, values, reverse order, VT_BOOL -1/0, an
  OLE string with an embedded #0), the results of every type converted back, and the copy-back of by-reference
  arguments including string and Boolean. No COM initialisation, no system object. }
uses SysUtils, Windows, ActiveX, ComObj, TestKit, DispKit;

type
  TColor = (Red, Green, Blue);

  ITypes = dispinterface
    procedure Ints(A: ShortInt; B: SmallInt; C: Integer; D: Int64); dispid 1;
    procedure UInts(A: Byte; B: Word; C: Cardinal; D: UInt64); dispid 2;
    procedure Reals(A: Single; B: Double; C: Currency; D: TDateTime); dispid 3;
    procedure Others(A: Boolean; B: WordBool; C: TColor; const S: string); dispid 4;
    procedure Refs(const A: IDispatch; const B: IInterface); dispid 5;
    procedure VarInts(var A: ShortInt; var B: SmallInt; var C: Integer; var D: Int64); dispid 6;
    procedure VarUInts(var A: Byte; var B: Word; var C: Cardinal; var D: UInt64); dispid 7;
    procedure VarReals(var A: Single; var B: Double; var C: Currency; var D: TDateTime); dispid 8;
    procedure VarOthers(var A: Boolean; var B: WordBool; var C: TColor; var S: string; out O: string); dispid 9;
    procedure VarRefs(var A: IDispatch; var B: IInterface); dispid 10;
    procedure Nothing; dispid 11;
    { results: the server answers member 100 + n (200 + n) with a value of variant type n }
    function GetSmallInt: SmallInt; dispid 102;
    function GetInteger: Integer; dispid 103;
    function GetSingle: Single; dispid 104;
    function GetDouble: Double; dispid 105;
    function GetCurrency: Currency; dispid 106;
    function GetDate: TDateTime; dispid 107;
    function GetString: string; dispid 108;
    function GetDisp: IDispatch; dispid 109;
    function GetBoolean: Boolean; dispid 111;
    function GetUnknown: IInterface; dispid 113;
    function GetShortInt: ShortInt; dispid 116;
    function GetByte: Byte; dispid 117;
    function GetWord: Word; dispid 118;
    function GetCardinal: Cardinal; dispid 119;
    function GetInt64: Int64; dispid 120;
    function GetUInt64: UInt64; dispid 121;
    function GetColor: TColor; dispid 203;
    function GetWordBool: WordBool; dispid 211;
  end;

var
  Srv, OtherSrv: TServer;
  Keep, OtherRef: IDispatch;
  D: ITypes;
  SelfCount, OtherCount: Integer;

{ the server as an IInterface reference; a function, so that the temporary reference ends here }
function SelfUnk: Pointer;
begin
  Result := Pointer(Keep as IInterface);
end;

{ the reference counts of both objects are what they were at the start: nothing leaked, nothing released twice }
procedure CheckCounts(const Name: string);
begin
  CheckEqI(SelfCount, Srv.RefCount, Name + ': the server''s reference count');
  CheckEqI(OtherCount, OtherSrv.RefCount, Name + ': the other object''s reference count');
end;

procedure ByValue;
var
  U: IInterface;
begin
  Section('by value: type numbers, values, reverse order (RTL-1014 step 1)');
  D.Nothing;
  CheckEqS('id=11 flags=1 args=0 named=0 noresult', Srv.Log, 'no arguments: zero IID, user locale, no result');
  D.Ints(-1, -2, -3, -4);
  CheckEqS('id=1 flags=1 args=4 named=0 noresult [20=-4] [3=-3] [2=-2] [16=-1]', Srv.Log, 'signed integers');
  D.Ints(127, 32767, 2147483647, 9223372036854775807);
  CheckEqS('id=1 flags=1 args=4 named=0 noresult [20=9223372036854775807] [3=2147483647] [2=32767] [16=127]',
    Srv.Log, 'their largest values');
  D.UInts(255, 65535, 4294967295, 18446744073709551615);
  CheckEqS('id=2 flags=1 args=4 named=0 noresult [21=FFFFFFFFFFFFFFFF] [19=4294967295] [18=65535] [17=255]',
    Srv.Log, 'unsigned integers');
  D.Reals(1.5, -2.25, 12.34, 36526.5);
  CheckEqS('id=3 flags=1 args=4 named=0 noresult [7=146106/4] [6=123400] [5=-9/4] [4=6/4]', Srv.Log,
    'Single, Double, Currency, TDateTime');
  D.Others(True, True, Blue, 'te'#0'xt');
  CheckEqS('id=4 flags=1 args=4 named=0 noresult [8=5:te~xt] [3=2] [11=-1] [11=-1]', Srv.Log,
    'True is -1 for Boolean and WordBool; the OLE string keeps #0');
  D.Others(False, False, Red, '');
  CheckEqS('id=4 flags=1 args=4 named=0 noresult [8=nil] [3=0] [11=0] [11=0]', Srv.Log,
    'False is 0; the empty string is nil');
  U := Keep;
  D.Refs(OtherRef, U);
  CheckEqS('id=5 flags=1 args=2 named=0 noresult [13=self] [9=other]', Srv.Log, 'interface references');
  D.Refs(nil, nil);
  CheckEqS('id=5 flags=1 args=2 named=0 noresult [13=nil] [9=nil]', Srv.Log, 'nil references');
end;

procedure ByReference;
var
  A1: ShortInt;
  A2: SmallInt;
  A3: Integer;
  A4: Int64;
  B1: Byte;
  B2: Word;
  B3: Cardinal;
  B4: UInt64;
  R1: Single;
  R2: Double;
  R3: Currency;
  R4: TDateTime;
  Bo: Boolean;
  Wb: WordBool;
  Co: TColor;
  S, O: string;
  DI: IDispatch;
  UI: IInterface;
begin
  Section('by reference: records and copy-back (RTL-1014 steps 1 and 6)');
  A1 := -5;
  A2 := -6;
  A3 := -7;
  A4 := -8;
  D.VarInts(A1, A2, A3, A4);
  CheckEqS('id=6 flags=1 args=4 named=0 noresult [&20=-8] [&3=-7] [&2=-6] [&16=-5]', Srv.Log, 'signed integers');
  CheckEqS('-4 -5 -6 -7', IntStr(A1) + ' ' + IntStr(A2) + ' ' + IntStr(A3) + ' ' + IntStr(A4), 'changed by the server');
  B1 := 200;
  B2 := 60000;
  B3 := 4000000000;
  B4 := 10;
  D.VarUInts(B1, B2, B3, B4);
  CheckEqS('id=7 flags=1 args=4 named=0 noresult [&21=000000000000000A] [&19=4000000000] [&18=60000] [&17=200]',
    Srv.Log, 'unsigned integers');
  CheckEqS('201 60001 4000000001 11', IntStr(B1) + ' ' + IntStr(B2) + ' ' + IntStr(B3) + ' ' + IntStr(Int64(B4)),
    'changed by the server');
  R1 := 1.5;
  R2 := -2.25;
  R3 := 12.34;
  R4 := 36526.5;
  D.VarReals(R1, R2, R3, R4);
  CheckEqS('id=8 flags=1 args=4 named=0 noresult [&7=146106/4] [&6=123400] [&5=-9/4] [&4=6/4]', Srv.Log,
    'real types');
  CheckEqF(3.0, R1, 0, 'Single doubled');
  CheckEqF(-4.5, R2, 0, 'Double doubled');
  CheckEqI(123401, PInt64(@R3)^, 'Currency plus one unit');
  CheckEqF(73053.0, R4, 0, 'TDateTime doubled');

  Bo := True;
  Wb := False;
  Co := Green;
  S := 'o'#0'ld';
  O := 'stale';
  D.VarOthers(Bo, Wb, Co, S, O);
  CheckEqS('id=9 flags=1 args=5 named=0 noresult [&8=nil] [&8=4:o~ld] [&3=1] [&11=0] [&11=-1]', Srv.Log,
    'Boolean as a 16-bit -1, WordBool, enumeration, string as an OLE string, an emptied out string');
  CheckEqB(False, Bo, 'Boolean copied back');
  CheckEqB(True, Wb, 'WordBool changed');
  CheckEqI(-1, PSmallInt(@Wb)^, 'its value is -1');
  CheckEqI(2, Ord(Co), 'enumeration changed');
  CheckEqS('o'#0'ld+new'#0'end', S, 'string copied back by length');
  CheckEqI(12, Length(S), 'its length');
  CheckEqS('+new'#0'end', O, 'out string copied back');
  Bo := False;
  Co := Red;
  S := '';
  D.VarOthers(Bo, Wb, Co, S, O);
  CheckEqS('id=9 flags=1 args=5 named=0 noresult [&8=nil] [&8=nil] [&3=0] [&11=-1] [&11=0]', Srv.Log,
    'False and the empty string by reference');
  CheckEqB(True, Bo, 'Boolean copied back as True');
  CheckEqB(False, Wb, 'WordBool back to False');

  DI := Keep;
  UI := nil;
  D.VarRefs(DI, UI);
  CheckEqS('id=10 flags=1 args=2 named=0 noresult [&13=nil] [&9=self]', Srv.Log, 'interface variables');
  Check(Pointer(DI) = Pointer(OtherRef), 'the IDispatch variable holds the other object');
  Check(Pointer(UI) = SelfUnk, 'the IInterface variable holds the server');
end;

procedure Results;
var
  S: string;
  DI: IDispatch;
  UI: IInterface;
  Cu: Currency;
  Wb: WordBool;
begin
  Section('results of every type (RTL-1014 steps 3 and 5)');
  Srv.Foreign := False;
  CheckEqI(-8, D.GetShortInt, 'ShortInt');
  CheckEqS('id=116 flags=1 args=0 named=0 result0', Srv.Log, 'the result record arrives empty');
  CheckEqI(-300, D.GetSmallInt, 'SmallInt');
  CheckEqI(-70000, D.GetInteger, 'Integer');
  CheckEqI(-5000000000, D.GetInt64, 'Int64');
  CheckEqI(200, D.GetByte, 'Byte');
  CheckEqI(60000, D.GetWord, 'Word');
  CheckEqI(4000000000, D.GetCardinal, 'Cardinal');
  Check(D.GetUInt64 = 18446744073709551614, 'UInt64');
  CheckEqF(1.5, D.GetSingle, 0, 'Single');
  CheckEqF(-2.25, D.GetDouble, 0, 'Double');
  Cu := D.GetCurrency;
  CheckEqI(123400, PInt64(@Cu)^, 'Currency');
  CheckEqF(36526.5, D.GetDate, 0, 'TDateTime');
  CheckEqB(True, D.GetBoolean, 'Boolean');
  Wb := D.GetWordBool;
  CheckEqI(-1, PSmallInt(@Wb)^, 'WordBool');
  CheckEqI(2, Ord(D.GetColor), 'enumeration');
  S := 'before';
  S := D.GetString;
  CheckEqS('re'#0'sult', S, 'string, copied by length');
  CheckEqI(7, Length(S), 'its length');

  DI := D.GetDisp;
  Check(Pointer(DI) = Pointer(OtherRef), 'IDispatch result');
  UI := D.GetUnknown;
  Check(Pointer(UI) = SelfUnk, 'IInterface result');
  { discarded results }
  D.GetString;
  D.GetDisp;

  Section('results given in another type are converted (RTL-1014 step 5)');
  Srv.Foreign := True;
  CheckEqI(-45, D.GetInteger, 'Integer from an OLE string');
  CheckEqS('123', D.GetString, 'string from an integer');
  CheckEqB(True, D.GetBoolean, 'Boolean from a non-zero integer');
  Wb := False;
  Wb := D.GetWordBool;
  CheckEqI(-1, PSmallInt(@Wb)^, 'WordBool from a non-zero integer is -1');
  CheckEqI(2, D.GetInt64, 'Int64 from a 16-bit integer');
  CheckEqI(2, D.GetByte, 'Byte from a 16-bit integer');
  CheckEqF(2.0, D.GetDouble, 0, 'Double from a 16-bit integer');
  CheckEqF(2.0, D.GetSingle, 0, 'Single from a 16-bit integer');
  Cu := D.GetCurrency;
  CheckEqI(20000, PInt64(@Cu)^, 'Currency from a 16-bit integer');
  CheckEqI(2, Ord(D.GetColor), 'enumeration from a 16-bit integer');
  DI := D.GetDisp;
  Check(Pointer(DI) = Pointer(Keep), 'IDispatch from an IUnknown record');
  UI := D.GetUnknown;
  Check(Pointer(UI) = SelfUnk, 'IInterface from an IDispatch record');
  Srv.Foreign := False;
end;

begin
  Srv := TServer.Create;
  Keep := Srv;
  OtherSrv := TServer.Create;
  OtherRef := OtherSrv;
  Srv.Other := OtherRef;
  D := Keep;

  SelfCount := Srv.RefCount;
  OtherCount := OtherSrv.RefCount;

  ByValue;
  CheckCounts('after the calls by value');
  ByReference;
  CheckCounts('after the calls by reference');
  Results;
  CheckCounts('after the results');

  D := nil;
  Srv.Other := nil;
  OtherRef := nil;
  Keep := nil;
  Done;
end.
