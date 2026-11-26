// phase: 8
// covers: RTL-1128, RTL-1130, RTL-1131, RTL-1132, RTL-1133, RTL-1135
program Cls_ReadInheritedErrors;
{ Errors of form inheritance in the reader (task R-248, ui-framework/phase8/form-inheritance.md section 3): F0109
  for an override block of the form scope that names no inherited component or one of another class, naming the
  nearest ancestor with a form (RTL-1128); F0112 for an "inherited" file of a class without an ancestor form
  (RTL-1131); F0113 for an "object" file of a class that inherits a form (RTL-1132); F0107 for an "inherited" object
  without a scope (RTL-1133); F0106 for a new component named like an inherited one; F0105 for a level-1 reference
  to a component that only level 2 adds (each level resolves its own references). After every failure the instance
  holds only what the earlier levels created, no flag of the reader is left and no Loaded was called (RTL-1130).
  The forms are registered by the program itself (_RegisterResources, ABI-150), so the compiler's form check of the
  root keyword plays no part. }
uses
  SysUtils, Classes, TestKit;

var
  Live: Integer;
  LoadedTotal: Integer;

type
  TNode = class(TComponent)
  private
    FCaption: string;
    FBuddy: TComponent;
  public
    LoadedCount: Integer;
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure Loaded; override;
  published
    property Caption: string read FCaption write FCaption;
    property Buddy: TComponent read FBuddy write FBuddy;
  end;

  TPart = class(TNode)
  end;

  TOther = class(TPart)
  end;

  TEBase = class(TNode)
  published
    A: TPart;
  end;

  TEUnknown = class(TEBase)
  end;

  { no form: the level read before TEGap's is TEBase's }
  TEMid = class(TEBase)
  end;

  TEGap = class(TEMid)
  end;

  TENoAnc = class(TNode)
  end;

  TEObj = class(TEBase)
  end;

  TENested = class(TEBase)
  end;

  TEPlain = class(TNode)
  end;

  TEDup = class(TEBase)
  end;

  TERefBase = class(TNode)
  published
    A: TPart;
    Later: TPart;
  end;

  TERef = class(TERefBase)
  end;

  TEGood = class(TEBase)
  end;

  { one entry of an embedded-resource table (ABI-150) }
  TFormEntry = record
    Name: Pointer;
    Data: Pointer;
    Size: NativeInt;
  end;

var
  Entries: array[0..15] of TFormEntry;
  EntryNames: array[0..15] of string;
  EntryBytes: array[0..15] of TBytes;
  EntryCount: Integer;

constructor TNode.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  Live := Live + 1;
end;

destructor TNode.Destroy;
begin
  Live := Live - 1;
  inherited Destroy;
end;

procedure TNode.Loaded;
begin
  inherited Loaded;
  LoadedCount := LoadedCount + 1;
  LoadedTotal := LoadedTotal + 1;
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

function Clean(C: TComponent): Boolean;
var
  I: Integer;
begin
  Result := not (csLoading in C.ComponentState) and not (csReading in C.ComponentState);
  for I := 0 to C.ComponentCount - 1 do
    if not Clean(C.Components[I]) then
      Result := False;
end;

{ reads the chain of AClass into a new instance: the exact message, what is left, flags, Loaded, leaks }
procedure Attempt(AClass: TComponentClass; const Expected: string; Left: Integer; const What: string);
var
  Inst: TComponent;
  Msg: string;
begin
  LoadedTotal := 0;
  Inst := AClass.Create(nil);
  Msg := '';
  try
    InitInheritedComponent(Inst, TNode);
  except
    on E: EReadError do
      Msg := E.Message;
  end;
  CheckEqS(Expected, Msg, What);
  CheckEqI(Left, Inst.ComponentCount, What + ': only the components of the earlier levels are left');
  CheckEqB(True, Clean(Inst), What + ': no loading flags');
  CheckEqI(0, LoadedTotal, What + ': no Loaded');
  Inst.Free;
  CheckEqI(0, Live, What + ': nothing leaked');
end;

var
  Gap: TEGap;
  Good: TEGood;
  Msg: string;
begin
  RegisterClasses([TPart]);
  AddForm('TEBase', 'object EBase: TEBase'#10'  Caption = ''base'''#10'  object A: TPart'#10'    Caption = ''a'''#10 +
    '  end'#10'end'#10);
  AddForm('TEUnknown', 'inherited EUnknown: TEUnknown'#10'  inherited Nope: TPart'#10'  end'#10'end'#10);
  AddForm('TEGap', 'inherited EGap: TEGap'#10'  inherited A: TOther'#10'  end'#10'end'#10);
  AddForm('TENoAnc', 'inherited ENoAnc: TENoAnc'#10'end'#10);
  AddForm('TEObj', 'object EObj: TEObj'#10'end'#10);
  AddForm('TENested', 'inherited ENested: TENested'#10'  object P: TPart'#10'    inherited A: TPart'#10 +
    '    end'#10'  end'#10'end'#10);
  AddForm('TEPlain', 'object EPlain: TEPlain'#10'  inherited A: TPart'#10'  end'#10'end'#10);
  AddForm('TEDup', 'inherited EDup: TEDup'#10'  object A: TPart'#10'  end'#10'end'#10);
  AddForm('TERefBase', 'object ERefBase: TERefBase'#10'  object A: TPart'#10'    Buddy = Later'#10'  end'#10'end'#10);
  AddForm('TERef', 'inherited ERef: TERef'#10'  object Later: TPart'#10'  end'#10'end'#10);
  AddForm('TEGood', 'inherited EGood: TEGood'#10'  inherited A: TPart'#10'    Caption = ''a good'''#10'  end'#10 +
    'end'#10);

  Section('F0109 in the form scope (RTL-1128)');
  Attempt(TEUnknown, 'TEUnknown.form(2,13): error F0109: Component Nope: TPart is not part of TEBase', 1,
    'unknown name');
  Attempt(TEGap, 'TEGap.form(2,13): error F0109: Component A: TOther is not part of TEBase', 1,
    'wrong class, named after the nearest ancestor with a form');
  Gap := TEGap.Create(nil);
  Msg := '';
  try
    ReadComponentFromText('inherited EGap: TEGap'#10'  inherited Nope: TPart'#10'  end'#10'end'#10, Gap, 'Text.form');
  except
    on E: EReadError do
      Msg := E.Message;
  end;
  CheckEqS('Text.form(2,13): error F0109: Component Nope: TPart is not part of TEMid', Msg,
    'read outside a chain: named after the class parent');
  Gap.Free;

  Section('F0112 and F0113 (RTL-1131, 1132, 1135)');
  Attempt(TENoAnc, 'TENoAnc.form(1,1): error F0112: Class TENoAnc has no ancestor form: its form file cannot start ' +
    'with "inherited"', 0, 'F0112');
  Attempt(TEObj, 'TEObj.form(1,1): error F0113: Class TEObj inherits the form of TEBase: its form file must start ' +
    'with "inherited"', 1, 'F0113');

  Section('F0107 without a scope (RTL-1133)');
  Attempt(TENested, 'TENested.form(3,5): error F0107: "inherited" objects are not supported', 1,
    'inside a new object');
  Attempt(TEPlain, 'TEPlain.form(2,3): error F0107: "inherited" objects are not supported', 0,
    'inside a file with an object root');

  Section('F0106 and F0105 across levels (RTL-1130)');
  Attempt(TEDup, 'TEDup.form(2,10): error F0106: A component named A already exists', 1,
    'a new component named like an inherited one');
  Attempt(TERef, 'TERefBase.form(3,13): error F0105: Component Later not found', 0,
    'a level-1 reference to a component of level 2');

  Section('a valid chain after the failures');
  LoadedTotal := 0;
  Good := TEGood.Create(nil);
  CheckEqB(True, InitInheritedComponent(Good, TNode), 'found');
  CheckEqS('EGood', Good.Name, 'name');
  CheckEqS('base|a good', TNode(Good).Caption + '|' + Good.A.Caption, 'values');
  CheckEqI(2, LoadedTotal, 'one Loaded for the root and one for A');
  CheckEqB(True, Clean(Good), 'no loading flags');
  Good.Free;
  CheckEqI(0, Live, 'nothing leaked');
  Done;
end.
