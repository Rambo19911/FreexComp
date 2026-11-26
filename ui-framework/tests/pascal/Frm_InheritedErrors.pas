// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1455
// phase: 8
program Frm_InheritedErrors;
{ U-160, ui-framework/phase8/form-inheritance.md UIF-1455 (with RTL-1128, 1130...1132): a reader error of any level
  of a form chain escapes the form constructor as EReadError with the exact position - F0112 (an "inherited" file
  of a class without an ancestor form), F0113 (an "object" file of a class that inherits a form), F0109 (an override
  block naming no inherited component, or one of another class), F0110 (a new component inside an inherited frame
  instance) - both from TForm's constructor called directly and from Application.CreateForm; the half-built form is
  destroyed (its destructor runs once), no form of the failed construction is left in Screen.Forms or owned by
  Application, and a valid descendant is created afterwards. The same holds for a frame derived from a frame
  (TFrame's constructor; the owner keeps nothing). The base form is embedded; the faulty descendant files are
  registered by the program itself (_RegisterResources, ABI-150), so the compiler's form check of the root keyword
  plays no part. Needs no desktop: no form is shown. }
uses
  SysUtils, Classes, Controls, Forms, StdCtrls, TestKit, Frm_AddressFrame;

var
  Destroyed: Integer;

type
  { no form data: counts destructions }
  TCountedForm = class(TForm)
  public
    destructor Destroy; override;
  end;

  TErrBase = class(TCountedForm)
  published
    OkButton: TButton;
    Address: TAddressFrame;
  end;

  TNoAncForm = class(TCountedForm)
  end;

  TObjForm = class(TErrBase)
  end;

  TUnknownForm = class(TErrBase)
  end;

  TWrongClassForm = class(TErrBase)
  end;

  TClosedForm = class(TErrBase)
  published
    Extra: TButton;
  end;

  TGoodForm = class(TErrBase)
  end;

  TErrFrame = class(TFrame)
  published
    Title: TLabel;
  end;

  TErrFrameEx = class(TErrFrame)
  end;

  { one entry of an embedded-resource table (ABI-150) }
  TFormEntry = record
    Name: Pointer;
    Data: Pointer;
    Size: NativeInt;
  end;

{$FORM Frm_InheritedErrorsBase.form}

var
  Entries: array[0..7] of TFormEntry;
  EntryNames: array[0..7] of string;
  EntryBytes: array[0..7] of TBytes;
  EntryCount: Integer;

destructor TCountedForm.Destroy;
begin
  Destroyed := Destroyed + 1;
  inherited Destroy;
end;

{ registers Text as the form of the class named ClassName; the table entry and its strings stay alive }
procedure AddForm(const ClassName, Text: string);
begin
  EntryNames[EntryCount] := ClassName;
  EntryBytes[EntryCount] := TEncoding.UTF8.GetBytes(Text);
  Entries[EntryCount].Name := Pointer(EntryNames[EntryCount]);
  Entries[EntryCount].Data := Pointer(EntryBytes[EntryCount]);
  Entries[EntryCount].Size := Length(EntryBytes[EntryCount]);
  _RegisterResources(@Entries[EntryCount], 1);
  EntryCount := EntryCount + 1;
end;

{ constructs AClass directly, then through Application.CreateForm: the exception, what is left, the destructor }
procedure Attempt(AClass: TComponentClass; const Expected, What: string);
var
  Msg: string;
  Forms, Owned: Integer;
  Ref: TComponent;
begin
  Forms := Screen.FormCount;
  Owned := Application.ComponentCount;
  Destroyed := 0;
  Msg := '';
  try
    AClass.Create(nil);
  except
    on E: Exception do
      Msg := E.ClassName + ': ' + E.Message;
  end;
  CheckEqS('EReadError: ' + Expected, Msg, What + ': the constructor raises');
  CheckEqI(1, Destroyed, What + ': the half-built form is destroyed');
  CheckEqI(Forms, Screen.FormCount, What + ': not left in Screen.Forms');
  Destroyed := 0;
  Msg := '';
  Ref := nil;
  try
    Application.CreateForm(AClass, Ref);
  except
    on E: Exception do
      Msg := E.ClassName + ': ' + E.Message;
  end;
  CheckEqS('EReadError: ' + Expected, Msg, What + ': CreateForm raises');
  CheckEqI(1, Destroyed, What + ': CreateForm destroys the half-built form');
  Check(Ref = nil, What + ': CreateForm clears the reference');
  CheckEqI(Forms, Screen.FormCount, What + ': CreateForm leaves nothing in Screen.Forms');
  CheckEqI(Owned, Application.ComponentCount, What + ': Application owns nothing new');
end;

var
  Good: TGoodForm;
  Host: TForm;
  Msg: string;
begin
  RegisterClasses([TErrFrame]);
  AddForm('TNoAncForm', 'inherited NoAncForm: TNoAncForm'#10'end'#10);
  AddForm('TObjForm', 'object ObjForm: TObjForm'#10'  Caption = ''obj'''#10'end'#10);
  AddForm('TUnknownForm', 'inherited UnknownForm: TUnknownForm'#10'  Caption = ''unknown'''#10 +
    '  inherited Nope: TButton'#10'  end'#10'end'#10);
  AddForm('TWrongClassForm', 'inherited WrongClassForm: TWrongClassForm'#10'  inherited OkButton: TEdit'#10 +
    '  end'#10'end'#10);
  AddForm('TClosedForm', 'inherited ClosedForm: TClosedForm'#10'  inherited Address: TAddressFrame'#10 +
    '    object Extra: TButton'#10'    end'#10'  end'#10'end'#10);
  AddForm('TGoodForm', 'inherited GoodForm: TGoodForm'#10'  Caption = ''good'''#10'end'#10);
  AddForm('TErrFrame', 'object ErrFrame: TErrFrame'#10'  object Title: TLabel'#10'    Caption = ''title'''#10 +
    '  end'#10'end'#10);
  AddForm('TErrFrameEx', 'inherited ErrFrameEx: TErrFrameEx'#10'  inherited Gone: TLabel'#10'  end'#10'end'#10);

  Section('F0112 and F0113 (RTL-1131, 1132)');
  Attempt(TNoAncForm, 'TNoAncForm.form(1,1): error F0112: Class TNoAncForm has no ancestor form: its form file ' +
    'cannot start with "inherited"', 'F0112');
  Attempt(TObjForm, 'TObjForm.form(1,1): error F0113: Class TObjForm inherits the form of TErrBase: its form file ' +
    'must start with "inherited"', 'F0113');

  Section('F0109 and F0110 in a descendant file (RTL-1128, 1126)');
  Attempt(TUnknownForm, 'TUnknownForm.form(3,13): error F0109: Component Nope: TButton is not part of TErrBase',
    'F0109 unknown name');
  Attempt(TWrongClassForm, 'TWrongClassForm.form(2,13): error F0109: Component OkButton: TEdit is not part of ' +
    'TErrBase', 'F0109 another class');
  Attempt(TClosedForm, 'TClosedForm.form(3,5): error F0110: Components cannot be added to frame instance Address',
    'F0110 inside an inherited instance');

  Section('a valid descendant afterwards');
  Destroyed := 0;
  Good := TGoodForm.Create(nil);
  CheckEqS('GoodForm', Good.Name, 'the name of its file');
  CheckEqS('good', Good.Caption, 'its Caption');
  CheckEqS('OK', Good.OkButton.Caption, 'the inherited button');
  CheckEqI(1, Screen.FormCount, 'one form');
  Good.Free;
  CheckEqI(1, Destroyed, 'destroyed once');
  CheckEqI(0, Screen.FormCount, 'no form');

  Section('a frame derived from a frame (TFrame''s constructor, RTL-1134)');
  Host := TForm.CreateNew(nil);
  Msg := '';
  try
    TErrFrameEx.Create(Host);
  except
    on E: Exception do
      Msg := E.ClassName + ': ' + E.Message;
  end;
  CheckEqS('EReadError: TErrFrameEx.form(2,13): error F0109: Component Gone: TLabel is not part of TErrFrame', Msg,
    'F0109 out of the frame constructor');
  CheckEqI(0, Host.ComponentCount, 'the owner keeps nothing');
  Host.Free;
  Done;
end.
