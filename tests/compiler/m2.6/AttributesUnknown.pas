// covers: LANG-760, LANG-763
program AttributesUnknown;

type
  [Serializable]
  TFoo = class
    [Weak] Owner: TObject;
  end;

var
  F: TFoo;
begin
  F := TFoo.Create;
  F.Owner := nil;
  Writeln('ok');
  F.Free;
end.