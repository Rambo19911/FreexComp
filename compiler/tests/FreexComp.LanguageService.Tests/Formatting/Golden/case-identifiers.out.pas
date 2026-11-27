program CaseIdentifiers;

uses
  SysUtils;

type
  TCounter = record
    FValue: Integer;
  end;

function IncrementBy(var Counter: TCounter; Amount: Integer): Integer;
begin
  Counter.FValue := Counter.FValue + Amount;
  Result := Counter.FValue;
end;

var
  Total: TCounter;
  Text: string;
begin
  Total.FValue := 0;
  IncrementBy(Total, 2);
  Text := 'total' + inttostr(Total.FValue); { total stays in a comment }
  Writeln(Text, Length(Text), True);
end.
