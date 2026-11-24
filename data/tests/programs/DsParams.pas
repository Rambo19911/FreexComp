// covers: DAT-050, DAT-051, DAT-052
program DsParams;
{ Parameters: ParseSQL (names, string literals, quoted identifiers, comments, '::', duplicates, rebuilding the
  collection) and the typing and value access of a parameter. }
uses
  SysUtils, Classes, DB, DataTestUtils, TestDataSet;

type
  TCalls = class
    Params: TParams;
    Param: TParam;
    Sink: Double;
    procedure ByNameMissing;
    procedure ReadInteger;
    procedure ReadFloat;
    procedure ReadBoolean;
    procedure ReadDateTime;
    procedure ReadBytes;
    procedure ReadString;
  end;

procedure TCalls.ByNameMissing;
begin
  Params.ParamByName('zz');
end;

procedure TCalls.ReadInteger;
begin
  Sink := Param.AsInteger;
end;

procedure TCalls.ReadFloat;
begin
  Sink := Param.AsFloat;
end;

procedure TCalls.ReadBoolean;
begin
  if Param.AsBoolean then
    Sink := 1;
end;

procedure TCalls.ReadDateTime;
begin
  Sink := Param.AsDateTime;
end;

procedure TCalls.ReadBytes;
begin
  Sink := Length(Param.AsBytes);
end;

procedure TCalls.ReadString;
begin
  Sink := Length(Param.AsString);
end;

var
  Calls: TCalls;

function Names(Params: TParams): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Params.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + Params[I].Name;
  end;
end;

{ the parameter names ParseSQL finds in the text }
function Parse(const SQL: string): string;
var
  P: TParams;
begin
  P := TParams.Create;
  try
    if P.ParseSQL(SQL, True) <> SQL then
      Result := 'TEXT CHANGED'
    else
      Result := Names(P);
  finally
    P.Free;
  end;
end;

procedure CheckParse(const SQL, Expected: string);
var
  Shown: string;
  I: Integer;
begin
  { line breaks of the text are shown as '~' so that every check stays on one line }
  Shown := SQL;
  for I := 1 to Length(Shown) do
    if Shown[I] < ' ' then
      Shown[I] := '~';
  CheckEq(Parse(SQL), Expected, '<' + Shown + '>');
end;

{ 'ftInteger null=False bound=True' }
function Describe(P: TParam): string;
begin
  Result := FieldTypeName(P.DataType) + ' null=' + BoolStr(P.IsNull) + ' bound=' + BoolStr(P.Bound);
end;

procedure TestParse;
begin
  Section('ParseSQL: names');
  CheckParse('', '');
  CheckParse('SELECT 1', '');
  CheckParse('SELECT * FROM t WHERE a = :a AND b = :b_2', 'a,b_2');
  CheckParse(':first', 'first');
  CheckParse('(:a,:b);', 'a,b');
  CheckParse('x=:_p1+:Q9z', '_p1,Q9z');
  CheckParse('a - :p / :q', 'p,q');
  CheckParse('a = :1', '');
  CheckParse('a = : x', '');
  CheckParse('a = :', '');
  CheckParse('a = :p.q', 'p');

  Section('ParseSQL: duplicates');
  CheckParse(':x, :y, :x', 'x,y');
  CheckParse(':Name or :NAME or :name', 'Name');
  CheckParse(':b :a :b :a', 'b,a');

  Section('ParseSQL: literals, identifiers, comments');
  CheckParse('a = '':no'' AND b = :yes', 'yes');
  CheckParse('a = ''it''''s :no'' AND b = :yes', 'yes');
  CheckParse('a = '''' AND b = :yes', 'yes');
  CheckParse('a = ''never closed :no', '');
  CheckParse('"col:no" = :yes', 'yes');
  CheckParse('`col:no` = :yes', 'yes');
  CheckParse('[col:no] = :yes', 'yes');
  CheckParse('a = :p -- :no' + #10 + 'AND b = :q', 'p,q');
  CheckParse('a = :p -- :no', 'p');
  CheckParse('a = :p /* :no' + #10 + ':no */ AND b = :q', 'p,q');
  CheckParse('a = :p /* :no', 'p');
  CheckParse('/* '' */ :p', 'p');
  CheckParse('''/*'' :p', 'p');
  CheckParse('-- '' :no' + #13#10 + ':p', 'p');

  Section('ParseSQL: double colon');
  CheckParse('SELECT a::text, :p', 'p');
  CheckParse('a:::p', 'p');
  CheckParse('a:: :p', 'p');
end;

procedure TestRebuild;
var
  P: TParams;
  A: TParam;
begin
  Section('ParseSQL: rebuilding the collection');
  P := TParams.Create;
  CheckEqInt(P.Count, 0, 'a new collection is empty');
  CheckEq(P.ParseSQL('SELECT :a, :b', False), 'SELECT :a, :b', 'the text is returned unchanged');
  CheckEqInt(P.Count, 0, 'DoCreate = False creates nothing');
  P.ParseSQL('SELECT :a, :b, :c', True);
  CheckEq(Names(P), 'a,b,c', 'DoCreate = True creates the parameters');
  P.ParamByName('a').AsInteger := 5;
  P.ParamByName('c').AsString := 'see';
  A := P.ParamByName('a');
  P.ParseSQL('SELECT :d, :C, :A', False);
  CheckEq(Names(P), 'a,b,c', 'DoCreate = False leaves the collection');
  P.ParseSQL('SELECT :d, :C, :A', True);
  CheckEq(Names(P), 'd,C,A', 'rebuilt in the order of first occurrence');
  CheckEq(Describe(P[0]), 'ftUnknown null=True bound=False', 'a new parameter is unbound');
  CheckEq(Describe(P[2]), 'ftInteger null=False bound=True', 'a parameter of the same name keeps type and state');
  CheckEqInt(P[2].AsInteger, 5, 'and its value');
  CheckEq(P[1].AsString, 'see', 'whatever the case of the name');
  Check(P.FindParam('b') = nil, 'a parameter that is no longer named is removed');
  Check(P.ParamByName('a') = A, 'the kept parameter is the same object');
  P.ParseSQL('', True);
  CheckEqInt(P.Count, 0, 'no parameters in the text: empty');

  Section('lookup');
  P.ParseSQL(':one :Two', True);
  Check(P.ParamByName('TWO') = P[1], 'ParamByName ignores case');
  Check(P.FindParam('ONE') = P[0], 'FindParam ignores case');
  Check(P.FindParam('zz') = nil, 'FindParam: nil when missing');
  Calls.Params := P;
  CheckRaises(Calls.ByNameMissing, 'EDatabaseError', 'Parameter ''zz'' not found', 'ParamByName: missing');
  P.Free;
end;

procedure TestTyping;
var
  Params: TParams;
  P: TParam;
  B: TBytes;
  D: TDateTime;
begin
  Section('typing');
  Params := TParams.Create;
  Params.ParseSQL(':p', True);
  P := Params[0];
  Calls.Param := P;
  CheckEq(P.Name, 'p', 'Name');
  CheckEq(Describe(P), 'ftUnknown null=True bound=False', 'never assigned: unbound and null');
  CheckEq(P.AsString + '|' + IntToStr(P.AsInteger) + '|' + IntToStr(P.AsLargeInt) + '|' + FloatToStr(P.AsFloat) + '|' +
    BoolStr(P.AsBoolean) + '|' + FloatToStr(P.AsDateTime) + '|' + IntToStr(Length(P.AsBytes)), '|0|0|0|False|0|0',
    'a null parameter reads as defaults');
  P.AsString := 'text';
  CheckEq(Describe(P), 'ftString null=False bound=True', 'AsString');
  CheckEq(P.AsString, 'text', 'the string value');
  P.AsInteger := 42;
  CheckEq(Describe(P), 'ftInteger null=False bound=True', 'AsInteger');
  CheckEqInt(P.AsInteger, 42, 'the integer value');
  P.AsLargeInt := 5000000000;
  CheckEq(Describe(P), 'ftLargeint null=False bound=True', 'AsLargeInt');
  CheckEqInt(P.AsLargeInt, 5000000000, 'the 64-bit value');
  P.AsFloat := 2.5;
  CheckEq(Describe(P), 'ftFloat null=False bound=True', 'AsFloat');
  CheckEq(FloatToStr(P.AsFloat), '2.5', 'the float value');
  P.AsBoolean := True;
  CheckEq(Describe(P), 'ftBoolean null=False bound=True', 'AsBoolean');
  CheckEqBool(P.AsBoolean, True, 'the boolean value');
  D := EncodeDate(2001, 2, 3) + 0.25;
  P.AsDateTime := D;
  CheckEq(Describe(P), 'ftDateTime null=False bound=True', 'AsDateTime');
  Check(P.AsDateTime = D, 'the date-time value');
  SetLength(B, 2);
  B[0] := 7;
  B[1] := 9;
  P.AsBytes := B;
  CheckEq(Describe(P), 'ftBlob null=False bound=True', 'AsBytes');
  Check((Length(P.AsBytes) = 2) and (P.AsBytes[0] = 7) and (P.AsBytes[1] = 9), 'the bytes');
  P.Clear;
  CheckEq(Describe(P), 'ftBlob null=True bound=True', 'Clear: null, bound, the type stays');
  CheckEqInt(Length(P.AsBytes), 0, 'cleared');
  P.Bound := False;
  CheckEq(Describe(P), 'ftBlob null=True bound=False', 'Bound can be reset');
  P.Clear;
  CheckEqBool(P.Bound, True, 'Clear binds an unbound parameter');

  Section('reading as another type');
  P.AsInteger := 42;
  CheckEq(P.AsString + '|' + IntToStr(P.AsLargeInt) + '|' + FloatToStr(P.AsFloat) + '|' + BoolStr(P.AsBoolean), '42|42|42|True',
    'an integer');
  P.AsLargeInt := 5000000000;
  CheckRaises(Calls.ReadInteger, 'EDatabaseError', 'Cannot access parameter ''p'' as type Integer', 'a 64-bit value as Integer');
  P.AsBoolean := False;
  CheckEq(P.AsString + '|' + IntToStr(P.AsInteger), 'False|0', 'a boolean');
  P.AsFloat := 2.5;
  CheckEq(P.AsString, FloatToStr(2.5), 'a float as string');
  CheckRaises(Calls.ReadInteger, 'EDatabaseError', 'Cannot access parameter ''p'' as type Integer', 'a float as Integer');
  CheckRaises(Calls.ReadBoolean, 'EDatabaseError', 'Cannot access parameter ''p'' as type Boolean', 'a float as Boolean');
  P.AsDateTime := D;
  CheckEq(P.AsString, DateTimeToStr(D), 'a date-time as string');
  Check(P.AsFloat = D, 'a date-time as float');
  P.AsString := '17';
  CheckEq(IntToStr(P.AsInteger) + '|' + IntToStr(P.AsLargeInt) + '|' + FloatToStr(P.AsFloat), '17|17|17', 'a numeric string');
  P.AsString := 'true';
  CheckEqBool(P.AsBoolean, True, 'a boolean string');
  P.AsString := 'abc';
  CheckRaises(Calls.ReadInteger, 'EDatabaseError', 'Cannot access parameter ''p'' as type Integer', 'a text as Integer');
  CheckRaises(Calls.ReadFloat, 'EDatabaseError', 'Cannot access parameter ''p'' as type Float', 'a text as Float');
  CheckRaises(Calls.ReadBoolean, 'EDatabaseError', 'Cannot access parameter ''p'' as type Boolean', 'a text as Boolean');
  CheckRaises(Calls.ReadDateTime, 'EDatabaseError', 'Cannot access parameter ''p'' as type DateTime', 'a text as DateTime');
  CheckEqInt(Length(P.AsBytes), 3, 'a string as bytes: UTF-8');
  P.AsBytes := B;
  CheckRaises(Calls.ReadString, 'EDatabaseError', 'Cannot access parameter ''p'' as type String', 'bytes as String');
  P.AsInteger := 1;
  CheckRaises(Calls.ReadBytes, 'EDatabaseError', 'Cannot access parameter ''p'' as type Bytes', 'an integer as Bytes');
  CheckRaises(Calls.ReadDateTime, 'EDatabaseError', 'Cannot access parameter ''p'' as type DateTime', 'an integer as DateTime');
  Params.Free;
end;

begin
  Calls := TCalls.Create;
  TestParse;
  TestRebuild;
  TestTyping;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
