// covers: LANG-991, LANG-992, LANG-998, CMP-1491
program HelperLibraryShape;

{ The helpers of UValueHelpers through the type name, on literals, on constants, on variables and on a const parameter. }

uses UValueHelpers;

const
  Big = Integer.MaxValue;

var
  V: Integer;
  S: string;
  Parts: TArray<string>;
  D: Double;

procedure ShowTrimmed(const S: string);
begin
  Writeln('const ', S.Trim.Length, ' ', S.IsEmpty);
end;

begin
  Writeln(Integer.MaxValue.ToString);
  Writeln(Big);
  Writeln(Integer.MinValue);
  Writeln('[', 42.ToString(5), ']');
  Writeln(Double.ToString(1.5), ' ', 2.25.ToString, ' ', Double.ToString(-3).Length);
  Writeln(Integer.Parse('12') + 1);
  Writeln(Integer.Parse('x'));
  Writeln(Integer.TryParse('x', V));
  Writeln(Integer.TryParse('-7', V), ' ', V);
  Writeln('  a,b '.Trim.Length);
  S := 'xyz';
  Writeln(S.Chars[0], S.Chars[2], ' ', S.Length);
  Writeln(string.Join('-', ['a', 'b']));
  Writeln(''.IsEmpty, ' ', S.IsEmpty);
  Writeln(True.ToString, ' ', (V > 0).ToString(TUseBoolStrs.True), ' ', False.ToString(TUseBoolStrs.False));
  Writeln('[', string.Create('*', 3), ']');
  Parts := 'a,bc,,d'.Split(',');
  Writeln(System.Length(Parts), ' ', string.Join('|', Parts));
  D := Double.Epsilon;
  Writeln(D > 0, ' ', D.IsPositive, ' ', D.ToString, ' ', 0.0.IsPositive, ' ', Double.MaxValue > 1E308);
  ShowTrimmed('  pad  ');
end.
