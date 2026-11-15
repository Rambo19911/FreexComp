// covers: RTL-013, RTL-086
program Sys_OpenArrays;
{ system.md sections 1.3 and 9: open array parameters arrive as (pointer to the first element, High index) - from a
  static array, a dynamic array, an open array constructor and an empty dynamic array; "array of const" arrives as
  an open array of TVarRec whose VType follows the argument mapping of rtl-abi.md section 9.10, with the payload at
  offset 8. }
uses TestKit;

type
  TAnimal = class
  end;
  TColour = (Red, Green, Blue);

var
  Static: array[3..7] of Integer;
  Dyn: array of Integer;
  Empty: array of Integer;
  I: Integer;
  First: PInteger;
  Animal: TAnimal;
  S: string;
  A: AnsiString;
  SS: ShortString;
  C: Char;
  AC: AnsiChar;
  B: Byte;
  I64: Int64;
  D: Double;
  P: Pointer;
  PC: PChar;
  PAC: PAnsiChar;

function SumOf(const Values: array of Integer; out Count: Integer; out Base: PInteger): Integer;
var
  K: Integer;
begin
  Count := High(Values) + 1;
  Base := nil;
  if Count > 0 then
    Base := @Values[0];
  Result := 0;
  for K := 0 to High(Values) do
    Result := Result + Values[K];
end;

procedure CheckArray(const Values: array of Integer; ExpectedHigh, ExpectedSum: Integer; ExpectedBase: PInteger;
  const Name: string);
var
  Count: Integer;
  Base: PInteger;
begin
  CheckEqI(ExpectedHigh, High(Values), Name + ': High');
  CheckEqI(0, Low(Values), Name + ': Low');
  CheckEqI(ExpectedHigh + 1, Length(Values), Name + ': Length');
  CheckEqI(ExpectedSum, SumOf(Values, Count, Base), Name + ': sum through a nested open array');
  if ExpectedBase <> nil then
    Check(Base = ExpectedBase, Name + ': pointer to the caller''s first element');
end;

{ VType of every element, as a list }
function Kinds(const Args: array of const): string;
var
  K: Integer;
begin
  Result := '';
  for K := 0 to High(Args) do
  begin
    if K > 0 then
      Result := Result + ' ';
    Result := Result + IntStr(Args[K].VType);
  end;
end;

function PayloadOffset(const Args: array of const): Integer;
begin
  Result := NativeInt(@Args[0].VInteger) - NativeInt(@Args[0]);
end;

function Payloads(const Args: array of const): string;
begin
  Result := IntStr(Args[0].VInteger) + ' ';
  if Args[1].VBoolean then
    Result := Result + 'True';
  Result := Result + ' ' + IntStr(Args[2].VInt64^) + ' ' + string(PChar(Args[3].VUnicodeString)) + ' '
    + IntStr(Round(Args[4].VExtended^ * 100)) + ' ' + Args[5].VWideChar;
  if Args[6].VObject = Animal then
    Result := Result + ' object';
  if Args[7].VClass = TAnimal then
    Result := Result + ' class';
end;

begin
  Section('open arrays: pointer and High');
  for I := Low(Static) to High(Static) do
    Static[I] := I;
  CheckArray(Static, 4, 3 + 4 + 5 + 6 + 7, @Static[3], 'static array 3..7');
  SetLength(Dyn, 3);
  Dyn[0] := 10;
  Dyn[1] := 20;
  Dyn[2] := 30;
  CheckArray(Dyn, 2, 60, @Dyn[0], 'dynamic array');
  CheckArray([1, 2, 3, 4], 3, 10, nil, 'open array constructor');
  CheckArray(Empty, -1, 0, nil, 'empty dynamic array');
  First := nil;
  CheckEqI(0, SumOf(Empty, I, First), 'empty: sum');
  Check(First = nil, 'empty: no element pointer');

  Section('array of const: TVarRec');
  CheckEqI(16, SizeOf(TVarRec), 'SizeOf(TVarRec)');
  CheckEqI(8, PayloadOffset([1]), 'payload at offset 8');
  Animal := TAnimal.Create;
  S := 'uni';
  A := 'ansi';
  SS := 'short';
  C := 'w';
  AC := 'a';
  B := 200;
  I64 := Int64(1) shl 40;
  D := 2.5;
  P := @I;
  PC := 'p';
  PAC := 'q';
  CheckEqS('0 0 0 0', Kinds([1, B, -5, Green]), 'integers of 32 bits or less and enumerations: vtInteger');
  CheckEqS('16 1', Kinds([I64, True]), 'Int64: vtInt64; Boolean: vtBoolean');
  CheckEqS('9 2', Kinds([C, AC]), 'Char: vtWideChar; AnsiChar: vtChar');
  CheckEqS('3 3', Kinds([D, 1.5]), 'reals: vtExtended');
  CheckEqS('17 17 11 4', Kinds([S, 'lit', A, SS]), 'string, literal: vtUnicodeString; AnsiString; ShortString');
  CheckEqS('10 6', Kinds([PC, PAC]), 'PChar: vtPWideChar; PAnsiChar: vtPChar');
  CheckEqS('7 8 5 5', Kinds([Animal, TAnimal, P, nil]), 'object, class, pointer, nil');
  CheckEqS('7 True 1099511627776 uni 250 w object class', Payloads([7, True, I64, S, D, C, Animal, TAnimal]),
    'payloads');
  CheckEqS('', Kinds([]), 'empty array of const');
  Animal.Free;
  Done;
end.
