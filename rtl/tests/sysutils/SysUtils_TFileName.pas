// phase: 4
// covers: LANG-205
program SysUtils_TFileName;
{ R-213: SysUtils.TFileName = type string (LANG-205: a distinct type, assignment-compatible with string both ways;
  RTTI names it TFileName). }
uses
  SysUtils, TypInfo, TestKit;

var
  F: TFileName;
  S: string;
  TI: PTypeInfo;
begin
  Section('declared, assignment-compatible with string');
  F := 'C:\dir\file.txt';
  S := F;
  CheckEqS('C:\dir\file.txt', S, 'TFileName -> string');
  S := 'D:\other\name.pas';
  F := S;
  CheckEqS('D:\other\name.pas', F, 'string -> TFileName');
  F := F + '.bak';
  CheckEqI(21, Length(F), 'Length of a TFileName');

  Section('passed to path functions');
  CheckEqS('name.pas.bak', ExtractFileName(F), 'ExtractFileName');
  CheckEqS('D:\other\', ExtractFilePath(F), 'ExtractFilePath');
  CheckEqS('.bak', ExtractFileExt(F), 'ExtractFileExt');
  F := ChangeFileExt(F, '.txt');
  CheckEqS('D:\other\name.pas.txt', F, 'ChangeFileExt result into a TFileName');

  Section('type info');
  { GetTypeName(TypeInfo(TFileName)) should be 'TFileName' (LANG-205); the compiler still emits the RTTI of string
    for a 'type string' declaration, reported to the lead with R-213, so only the kind is checked here. }
  TI := TypeInfo(TFileName);
  Check(GetTypeKind(TI) = tkUString, 'kind is tkUString');
  Done;
end.
