// covers: RTL-270, RTL-271
program SU_StringBuilder;
{ sysutils.md section 9: constructors and capacity, every Append overload, AppendFormat, AppendLine, Insert, Remove,
  Replace, Clear, EnsureCapacity, ToString (whole and range), Length (growing fills #0), Capacity, MaxCapacity, Chars
  (0-based, default property), growth (RTL-271) and the range errors of RTL-270 with exact messages. }
uses
  SysUtils, TestKit;

type
  TNamed = class
  public
    function ToString: string; override;
  end;

function TNamed.ToString: string;
begin
  Result := 'named';
end;

var
  Msg: string;

procedure Expect(const Expected, Name: string);
begin
  CheckEqS(Expected, Msg, Name);
  Msg := 'no exception';
end;

procedure Caught(E: Exception);
begin
  Msg := E.ClassName + ': ' + E.Message;
end;

procedure TestCreate;
var
  B: TStringBuilder;
begin
  Section('constructors, capacity');
  B := TStringBuilder.Create;
  CheckEqI(16, B.Capacity, 'Create: capacity 16');
  CheckEqI(0, B.Length, 'Create: empty');
  CheckEqS('', B.ToString, 'Create: ToString');
  CheckEqI(MaxInt, B.MaxCapacity, 'MaxCapacity');
  B.Free;
  B := TStringBuilder.Create(100);
  CheckEqI(100, B.Capacity, 'Create(100)');
  B.Free;
  B := TStringBuilder.Create('hello');
  CheckEqS('hello', B.ToString, 'Create(Value)');
  CheckEqI(5, B.Length, 'Create(Value) length');
  Check(B.Capacity >= 5, 'Create(Value) capacity holds the value');
  B.Free;
end;

procedure TestAppend;
var
  B: TStringBuilder;
  O: TNamed;
  Big: Int64;
begin
  Section('Append');
  B := TStringBuilder.Create;
  O := TNamed.Create;
  Big := 1234567890123;
  B.Append('ab').Append('c').Append('x', 3).Append(-42).Append(Big).Append(True).Append(False);
  CheckEqS('abcxxx-421234567890123TrueFalse', B.ToString, 'string, Char, repeat, Integer, Int64, Boolean');
  B.Clear;
  B.Append(2.5).Append(' ').Append(O).Append(TObject(nil)).Append('!');
  CheckEqS('2.5 named!', B.ToString, 'Double, TObject, nil');
  B.Clear;
  B.AppendFormat('%d-%s', [7, 'z']);
  CheckEqS('7-z', B.ToString, 'AppendFormat');
  B.Clear;
  B.AppendLine('one').AppendLine;
  CheckEqS('one' + sLineBreak + sLineBreak, B.ToString, 'AppendLine');
  B.Clear;
  B.Append('y', 0);
  CheckEqS('', B.ToString, 'repeat count 0');
  O.Free;
  B.Free;
end;

procedure TestEdit;
var
  B: TStringBuilder;
begin
  Section('Insert, Remove, Replace, Chars');
  B := TStringBuilder.Create('hello world');
  B.Insert(0, '>').Insert(12, '<').Insert(6, ',');
  CheckEqS('>hello, world<', B.ToString, 'Insert at start, end, middle');
  B.Remove(0, 1).Remove(12, 1).Remove(5, 1);
  CheckEqS('hello world', B.ToString, 'Remove');
  B.Remove(3, 0);
  CheckEqS('hello world', B.ToString, 'Remove 0 characters');
  B.Replace('o', '0');
  CheckEqS('hell0 w0rld', B.ToString, 'Replace(Char, Char)');
  B.Replace('l', 'LL');
  CheckEqS('heLLLL0 w0rLLd', B.ToString, 'Replace(string, string): all, growing');
  B.Replace('LL', '');
  CheckEqS('he0 w0rd', B.ToString, 'Replace: shrinking to empty');
  B.Replace('', 'x');
  CheckEqS('he0 w0rd', B.ToString, 'Replace: empty OldValue changes nothing');
  B.Replace('aa', 'b');
  CheckEqS('he0 w0rd', B.ToString, 'Replace: no occurrence');
  CheckEqS('h', B.Chars[0], 'Chars[0]');
  CheckEqS('d', B[7], 'default property, last');
  B[0] := 'H';
  B.Chars[2] := 'o';
  CheckEqS('Heo w0rd', B.ToString, 'Chars write');
  CheckEqS('eo w', B.ToString(1, 4), 'ToString(StartIndex, StrLength)');
  CheckEqS('', B.ToString(8, 0), 'ToString empty at the end');
  B.Free;
end;

procedure TestLength;
var
  B: TStringBuilder;
begin
  Section('Length, Capacity, growth');
  B := TStringBuilder.Create('abc');
  B.Length := 5;
  CheckEqS('abc' + #0 + #0, B.ToString, 'growing Length fills #0');
  B.Length := 2;
  CheckEqS('ab', B.ToString, 'shrinking Length');
  B.Free;
  B := TStringBuilder.Create(4);
  B.Append('abc');
  CheckEqI(4, B.Capacity, 'fits');
  B.Append('de');
  CheckEqI(8, B.Capacity, 'growth: Capacity * 2');
  B.Append('0123456789');
  CheckEqI(16, B.Capacity, 'growth: Capacity * 2 again');
  B.Append(StringOfChar('x', 40));
  CheckEqI(55, B.Capacity, 'growth: needed when larger');
  CheckEqI(55, B.Length, 'length after growth');
  B.Clear;
  CheckEqI(0, B.Length, 'Clear: length 0');
  CheckEqI(55, B.Capacity, 'Clear keeps the capacity');
  CheckEqI(55, B.EnsureCapacity(10), 'EnsureCapacity smaller');
  CheckEqI(70, B.EnsureCapacity(70), 'EnsureCapacity larger');
  B.Append('abc');
  B.Capacity := 3;
  CheckEqI(3, B.Capacity, 'Capacity := Length');
  CheckEqS('abc', B.ToString, 'content kept');
  B.Free;
end;

procedure TestErrors;
var
  B: TStringBuilder;
begin
  Section('range errors');
  Msg := 'no exception';
  B := TStringBuilder.Create('abc');
  try B.Chars[3] := 'x'; except on E: Exception do Caught(E); end;
  Expect('EArgumentOutOfRangeException: Index out of range (3)', 'Chars write past the end');
  try B.ToString(-1, 1); except on E: Exception do Caught(E); end;
  Expect('EArgumentOutOfRangeException: Index out of range (-1)', 'ToString negative start');
  try B.ToString(1, 3); except on E: Exception do Caught(E); end;
  Expect('EArgumentOutOfRangeException: Index out of range (3)', 'ToString past the end');
  try B.Insert(4, 'x'); except on E: Exception do Caught(E); end;
  Expect('EArgumentOutOfRangeException: Index out of range (4)', 'Insert past the end');
  try B.Remove(1, -1); except on E: Exception do Caught(E); end;
  Expect('EArgumentOutOfRangeException: Index out of range (-1)', 'Remove negative length');
  try B.Remove(2, 2); except on E: Exception do Caught(E); end;
  Expect('EArgumentOutOfRangeException: Index out of range (2)', 'Remove past the end');
  try B.Length := -1; except on E: Exception do Caught(E); end;
  Expect('EArgumentOutOfRangeException: Index out of range (-1)', 'negative Length');
  try B.Append('x', -2); except on E: Exception do Caught(E); end;
  Expect('EArgumentOutOfRangeException: Index out of range (-2)', 'negative repeat count');
  try B.Capacity := 2; except on E: Exception do Caught(E); end;
  Expect('EArgumentOutOfRangeException: Capacity cannot be less than Length', 'Capacity < Length');
  CheckEqS('abc', B.ToString, 'unchanged after the errors');
  B.Free;
  try B := TStringBuilder.Create(-1); except on E: Exception do Caught(E); end;
  Expect('EArgumentOutOfRangeException: Index out of range (-1)', 'Create negative capacity');
end;

begin
  TestCreate;
  TestAppend;
  TestEdit;
  TestLength;
  TestErrors;
  Done;
end.
