program CaseIdentifiersOff;

uses
  SysUtils;

type
  TCounter = record
    FValue: Integer;
  end;

function IncrementBy(var Counter: TCounter; Amount: Integer): Integer;
begin
  counter.fvalue := COUNTER.FVALUE + amount;
  result := counter.FValue;
end;

var
  Total: tcounter;
  Text: string;
begin
  total.fValue := 0;
  incrementby(TOTAL, 2);
  text := 'total' + inttostr(total.FVALUE); { total stays in a comment }
  writeln(text, length(TEXT), TRUE);
end.
