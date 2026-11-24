// covers: LANG-569, LANG-570
program HintInlineHides;

{ An inline declaration that hides a field, a local of the enclosing routine or a global of the same unit gets hint
  H2011; hiding a name of the unit System gives none. The program runs: inside the scope the inline value is used,
  before the declaration and after the scope the outer one. }

type
  TCounter = class
    Count: Integer;
    procedure Show;
  end;

var
  Limit: Integer;

procedure TCounter.Show;
begin
  Count := 10;
  WriteLn('field before ', Count);
  begin
    var Count := 'inline';
    WriteLn('inside ', Count);
  end;
  WriteLn('field after ', Count);
end;

procedure Outer;
var
  Total: Integer;

  procedure Inner;
  begin
    WriteLn('outer local before ', Total);
    var Total := 2.5;
    WriteLn('inside ', Total:0:1);
  end;

begin
  Total := 20;
  Inner;
  WriteLn('outer local after ', Total);
end;

procedure UseGlobal;
begin
  WriteLn('global before ', Limit);
  if Limit > 0 then
  begin
    var Limit := 'text';
    WriteLn('inside ', Limit);
  end;
  WriteLn('global after ', Limit);
end;

procedure SystemName;
begin
  var Pi := 3;
  var Length := 4;
  WriteLn('system names ', Pi + Length);
end;

var
  C: TCounter;
begin
  Limit := 30;
  C := TCounter.Create;
  C.Show;
  C.Free;
  Outer;
  UseGlobal;
  SystemName;
  WriteLn('program block before ', Limit);
  var Limit := 31;
  WriteLn('program block inside ', Limit);
end.
