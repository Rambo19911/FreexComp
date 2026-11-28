program P;

uses UMain;

var
  F: TMainForm;
  D: Pointer;
  S: NativeInt;
begin
  Writeln('form ', FindResourceData('TMainForm', D, S));
  F := TMainForm.Create;
  F.Button1 := TButton.Create;
  F.ButtonClick(F.Button1);
  F.ButtonClick(nil);
  F.Button1.Free;
  F.Free;
end.
