// covers: LANG-993, LANG-992
program ErrHelperDistinctType;

{ The helper for Integer is no helper of a distinct type, of a subrange or of another integer type; on an Integer a name
  the helper does not declare is E2005; the sum of a subrange value and an integer is an Integer and takes its helper. }

type
  TMyInt = type Integer;
  TDigit = 0..9;

  TIntHelper = record helper for Integer
    function Twice: Integer;
  end;

function TIntHelper.Twice: Integer;
begin
  Result := Self * 2;
end;

var
  V: TMyInt;
  D: TDigit;
  B: Byte;
  I: Integer;
begin
  Writeln(V.Twice);
  Writeln(D.Twice);
  Writeln(B.Twice);
  Writeln(I.Missing);
  Writeln((D + 1).Twice);
end.
