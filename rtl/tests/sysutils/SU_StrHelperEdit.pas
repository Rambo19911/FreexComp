// phase: 8
// covers: RTL-1263, RTL-1268, RTL-1269
program SU_StrHelperEdit;
{ sysutils.md section 12, task R-271 (F10-030): the string helper, part 1 - Substring (= Copy from position + 1),
  Insert and Remove (the clipping of System.Insert and System.Delete on a copy), QuotedString / DeQuotedString
  (= QuotedStr, AnsiQuotedStr, AnsiDequotedStr) and the conversions ToBoolean, ToInteger, ToInt64, ToSingle,
  ToDouble, ToExtended with the exact EConvertError of the classic routine (block H of standard-helpers.md section
  5.2). Every receiver is a variable; no member changes it (X is compared after each group). Samples: X = 'abcdef',
  U = 'Gr' u-umlaut sharp-s 'e ' Zhe i (Cyrillic), P = 'a' U+1F600 (a pair) 'b', E = ''. }
uses
  SysUtils, TestKit;

var
  X, U, P, E, Q, V: string;
  FS: TFormatSettings;

function Msg(const Text, Kind: string): string;
begin
  Result := '''' + Text + ''' is not a valid ' + Kind + ' value';
end;

procedure IntFails(const S, Name: string);
var
  I: Integer;
begin
  Expect('EConvertError', Msg(S, 'integer'));
  try
    I := S.ToInteger;
    Writeln('# no exception, ', I);
  except
    on Ex: Exception do CheckRaises(Name, Ex.ClassName, Ex.Message);
  end;
end;

procedure Int64Fails(const S, Name: string);
var
  I: Int64;
begin
  Expect('EConvertError', Msg(S, 'integer'));
  try
    I := S.ToInt64;
    Writeln('# no exception, ', I);
  except
    on Ex: Exception do CheckRaises(Name, Ex.ClassName, Ex.Message);
  end;
end;

procedure BoolFails(const S, Name: string);
var
  B: Boolean;
begin
  Expect('EConvertError', Msg(S, 'boolean'));
  try
    B := S.ToBoolean;
    Writeln('# no exception, ', B);
  except
    on Ex: Exception do CheckRaises(Name, Ex.ClassName, Ex.Message);
  end;
end;

{ Kind 0: ToDouble, 1: ToDouble(FS), 2: ToSingle, 3: ToSingle(FS), 4: ToExtended, 5: ToExtended(FS) }
procedure FloatFails(const S: string; Kind: Integer; const Name: string);
var
  D: Double;
begin
  Expect('EConvertError', Msg(S, 'floating point'));
  try
    case Kind of
      0: D := S.ToDouble;
      1: D := S.ToDouble(FS);
      2: D := S.ToSingle;
      3: D := S.ToSingle(FS);
      4: D := S.ToExtended;
    else
      D := S.ToExtended(FS);
    end;
    Writeln('# no exception, ', D);
  except
    on Ex: Exception do CheckRaises(Name, Ex.ClassName, Ex.Message);
  end;
end;

begin
  X := 'abcdef';
  U := 'Gr' + #$00FC + #$00DF + 'e ' + #$0416 + #$0438;
  P := 'a' + #$D83D + #$DE00 + 'b';
  E := '';
  FS := TFormatSettings.Invariant;
  FS.DecimalSeparator := ',';

  Section('Substring');
  CheckEqS('cdef', X.Substring(2), 'X.Substring(2)');
  CheckEqS('cde', X.Substring(2, 3), 'X.Substring(2, 3)');
  CheckEqS('abcdef', X.Substring(0), 'X.Substring(0)');
  CheckEqS('', X.Substring(6), 'X.Substring(6)');
  CheckEqS('', X.Substring(7), 'X.Substring(7)');
  CheckEqS('abcdef', X.Substring(-2), 'X.Substring(-2)');
  CheckEqS('abc', X.Substring(-2, 3), 'X.Substring(-2, 3) keeps the count');
  CheckEqS('ef', X.Substring(4, 10), 'X.Substring(4, 10)');
  CheckEqS('', X.Substring(2, 0), 'X.Substring(2, 0)');
  CheckEqS('', X.Substring(2, -1), 'X.Substring(2, -1)');
  CheckEqS(#$DE00 + 'b', P.Substring(2), 'P.Substring(2) cuts the pair');
  CheckEqS(#$00FC + #$00DF, U.Substring(2, 2), 'U.Substring(2, 2)');
  CheckEqS('', E.Substring(0), 'E.Substring(0)');
  CheckEqS('', E.Substring(0, 1), 'E.Substring(0, 1)');
  CheckEqS('abcdef', X, 'X is unchanged');

  Section('Insert');
  CheckEqS('-abcdef', X.Insert(0, '-'), 'X.Insert(0, -)');
  CheckEqS('abc--def', X.Insert(3, '--'), 'X.Insert(3, --)');
  CheckEqS('abcdef!', X.Insert(6, '!'), 'X.Insert(6, !)');
  CheckEqS('abcdef!', X.Insert(99, '!'), 'X.Insert(99, !)');
  CheckEqS('!abcdef', X.Insert(-5, '!'), 'X.Insert(-5, !)');
  CheckEqS('abcdef', X.Insert(2, ''), 'X.Insert(2, empty)');
  CheckEqS('x', E.Insert(0, 'x'), 'E.Insert(0, x)');
  CheckEqS('Gr' + #$00FC + '.' + #$00DF + 'e ' + #$0416 + #$0438, U.Insert(3, '.'), 'U.Insert(3, .)');
  CheckEqS('a' + #$D83D + '|' + #$DE00 + 'b', P.Insert(2, '|'), 'P.Insert(2, |) splits the pair');
  CheckEqS('abcdef', X, 'X is unchanged');

  Section('Remove');
  CheckEqS('ab', X.Remove(2), 'X.Remove(2)');
  CheckEqS('', X.Remove(0), 'X.Remove(0)');
  CheckEqS('abcdef', X.Remove(6), 'X.Remove(6)');
  CheckEqS('abcdef', X.Remove(-1), 'X.Remove(-1)');
  CheckEqS('adef', X.Remove(1, 2), 'X.Remove(1, 2)');
  CheckEqS('abcd', X.Remove(4, 10), 'X.Remove(4, 10)');
  CheckEqS('abcdef', X.Remove(2, 0), 'X.Remove(2, 0)');
  CheckEqS('abcdef', X.Remove(-1, 3), 'X.Remove(-1, 3) is not shifted');
  CheckEqS('abcdef', X.Remove(6, 1), 'X.Remove(6, 1)');
  CheckEqS('abcdef', X.Remove(2, -1), 'X.Remove(2, -1)');
  CheckEqS('a' + #$DE00 + 'b', P.Remove(1, 1), 'P.Remove(1, 1) cuts the pair');
  CheckEqS('Gr', U.Remove(2), 'U.Remove(2)');
  CheckEqS('', E.Remove(0), 'E.Remove(0)');
  CheckEqS('', E.Remove(0, 1), 'E.Remove(0, 1)');
  CheckEqS('abcdef', X, 'X is unchanged');

  Section('quoting');
  Q := 'it''s';
  CheckEqS('''it''''s''', Q.QuotedString, 'it''s.QuotedString');
  CheckEqS('"it''s"', Q.QuotedString('"'), 'it''s.QuotedString(")');
  V := '"a""b"';
  CheckEqS('a"b', V.DeQuotedString('"'), '"a""b".DeQuotedString(")');
  V := '''it''''s''';
  CheckEqS('it''s', V.DeQuotedString, '''it''''s''.DeQuotedString');
  CheckEqS('abcdef', X.DeQuotedString, 'X.DeQuotedString');
  CheckEqS('abcdef', X.DeQuotedString('"'), 'X.DeQuotedString(")');
  CheckEqS('''''', E.QuotedString, 'E.QuotedString');
  CheckEqS('""', E.QuotedString('"'), 'E.QuotedString(")');
  CheckEqS('', E.DeQuotedString, 'E.DeQuotedString');
  CheckEqS('''' + U + '''', U.QuotedString, 'U.QuotedString');
  CheckEqS(U, U.DeQuotedString, 'U.DeQuotedString');
  CheckEqS('it''s', Q, 'it''s is unchanged');

  Section('ToInteger');
  V := '42';
  CheckEqI(42, V.ToInteger, '42.ToInteger');
  V := ' -7 ';
  CheckEqI(-7, V.ToInteger, ' -7 .ToInteger');
  V := '$10';
  CheckEqI(16, V.ToInteger, '$10.ToInteger');
  IntFails('x', 'x.ToInteger');
  IntFails('', 'E.ToInteger');
  IntFails('9000000000', '9000000000.ToInteger');
  IntFails(U, 'U.ToInteger');

  Section('ToInt64');
  V := '9000000000';
  CheckEqI(9000000000, V.ToInt64, '9000000000.ToInt64');
  V := '-42';
  CheckEqI(-42, V.ToInt64, '-42.ToInt64');
  Int64Fails('x', 'x.ToInt64');
  Int64Fails('', 'E.ToInt64');

  Section('ToBoolean');
  V := 'true';
  CheckEqB(True, V.ToBoolean, 'true.ToBoolean');
  V := '0';
  CheckEqB(False, V.ToBoolean, '0.ToBoolean');
  V := 'FALSE';
  CheckEqB(False, V.ToBoolean, 'FALSE.ToBoolean');
  BoolFails('maybe', 'maybe.ToBoolean');
  BoolFails('', 'E.ToBoolean');

  Section('floats');
  V := '1.5';
  CheckEqF(1.5, V.ToDouble, 0, '1.5.ToDouble');
  V := '1,5';
  CheckEqF(1.5, V.ToDouble(FS), 0, '1,5.ToDouble(FS)');
  FloatFails('1,5', 0, '1,5.ToDouble');
  FloatFails('1.5', 1, '1.5.ToDouble(FS)');
  V := '0.5';
  CheckEqF(0.5, V.ToSingle, 0, '0.5.ToSingle');
  V := '0,25';
  CheckEqF(0.25, V.ToSingle(FS), 0, '0,25.ToSingle(FS)');
  V := '0.1';
  CheckEqF(Single(0.1), V.ToSingle, 0, '0.1.ToSingle is the nearest Single');
  FloatFails('1e39', 2, '1e39.ToSingle');
  FloatFails('1e39', 3, '1e39.ToSingle(FS)');
  V := '2.5';
  CheckEqF(2.5, V.ToExtended, 0, '2.5.ToExtended');
  V := '2,5';
  CheckEqF(2.5, V.ToExtended(FS), 0, '2,5.ToExtended(FS)');
  FloatFails('', 0, 'E.ToDouble');
  FloatFails('', 2, 'E.ToSingle');
  FloatFails('x', 4, 'x.ToExtended');
  FloatFails('x', 5, 'x.ToExtended(FS)');
  Done;
end.
