program P;

uses UMain;

var
  F: TMainForm;
  D: Pointer;
  S: NativeInt;
begin
  Writeln('form ', FindResourceData('TMainForm', D, S));
  F := TMainForm.Create;
  F.OkButton := TButton.Create;
  F.ButtonClick(F.OkButton);
  F.ButtonClick(nil);
  F.OkButton.Free;
  F.Free;
end.
