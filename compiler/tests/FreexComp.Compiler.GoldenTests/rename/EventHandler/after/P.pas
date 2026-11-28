program P;

uses UMain;

var
  F: TMainForm;
  D: Pointer;
  S: NativeInt;
begin
  Writeln('form ', FindResourceData('TMainForm', D, S));
  F := TMainForm.Create;
  F.FormCreate(nil);
  F.OkClick(nil);
  F.Free;
end.
