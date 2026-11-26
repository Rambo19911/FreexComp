// phase: 8
// covers: RTL-1093
program Cls_ReadMultiPartRef;
{ Component references of more than two parts (task R-252, RTL-1093, RTL-1104), as the designer writes and renames
  them: the first part is looked up in the innermost scope (the scope itself by name, then its components), else
  among the names of the file (its root, then the root's components); each further part is a component of the
  previous part's instance. So a form reaches the child of a frame nested in a frame instance with
  Outer1.Home.StreetEdit, and inside Outer1's lines with Home.StreetEdit; a frame's own file reaches its nested
  instance's child with Home.StreetEdit, for each instance on its own. A forward reference is a fix-up resolved the
  same way after the root's end; one still unresolved is F0105. }
uses
  SysUtils, Classes, TestKit;

type
  TNode = class(TComponent)
  private
    FBuddy: TComponent;
    FBuddy2: TComponent;
  published
    property Buddy: TComponent read FBuddy write FBuddy;
    property Buddy2: TComponent read FBuddy2 write FBuddy2;
  end;

  TPart = class(TNode)
  end;

  { stands for TFrame: a class without a resource of its own }
  TTestBase = class(TNode)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TAddressFrame = class(TTestBase)
  published
    StreetEdit: TPart;
  end;

  TOuterFrame = class(TTestBase)
  published
    Pad: TPart;
    Home: TAddressFrame;
  end;

  TMainRoot = class(TComponent)
  published
    Box: TPart;
    Outer1: TOuterFrame;
    Outer2: TOuterFrame;
    Late: TPart;
  end;

{$FORM Cls_ReadMultiPartRefAddress.form}
{$FORM Cls_ReadMultiPartRefOuter.form}

constructor TTestBase.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  InitInheritedComponent(Self, TTestBase, AOwner = nil);
end;

{ the owner chain below the root: Outer1.Home.StreetEdit }
function PathOf(C: TComponent): string;
begin
  if C = nil then
    Result := 'nil'
  else
  begin
    Result := C.Name;
    C := C.Owner;
    while (C <> nil) and (C.Owner <> nil) do
    begin
      Result := C.Name + '.' + Result;
      C := C.Owner;
    end;
  end;
end;

procedure Show(N: TNode);
begin
  WriteLn(PathOf(N), ': Buddy=', PathOf(N.Buddy), ' Buddy2=', PathOf(N.Buddy2));
end;

const
  MainText =
    'object Main: TMainRoot'#10 +
    '  object Box: TPart'#10 +
    '    Buddy = Outer2.Home.StreetEdit'#10 +
    '  end'#10 +
    '  inline Outer1: TOuterFrame'#10 +
    '    Buddy = Home.StreetEdit'#10 +
    '    inherited Pad: TPart'#10 +
    '      Buddy2 = Outer1.Home.StreetEdit'#10 +
    '    end'#10 +
    '  end'#10 +
    '  inline Outer2: TOuterFrame'#10 +
    '    Buddy = Home.StreetEdit'#10 +
    '    inherited Pad: TPart'#10 +
    '      Buddy2 = Outer1.Home.StreetEdit'#10 +
    '    end'#10 +
    '    inherited Home: TAddressFrame'#10 +
    '      Buddy = Home.StreetEdit'#10 +
    '    end'#10 +
    '  end'#10 +
    '  object Late: TPart'#10 +
    '    Buddy = Main.Outer1.Home.StreetEdit'#10 +
    '    Buddy2 = Outer1.Home'#10 +
    '  end'#10 +
    'end'#10;

  Unresolved =
    'object Main: TMainRoot'#10 +
    '  object Box: TPart'#10 +
    '    Buddy = Outer1.Home.Nope'#10 +
    '  end'#10 +
    '  inline Outer1: TOuterFrame'#10 +
    '  end'#10 +
    'end'#10;

var
  Main: TMainRoot;
  Msg: string;
begin
  RegisterClasses([TPart, TAddressFrame, TOuterFrame, TMainRoot]);

  Section('references of three and more parts');
  Main := TMainRoot.Create(nil);
  ReadComponentFromText(MainText, Main, 'Main.form');
  Show(Main.Box);
  Show(Main.Outer1);
  Show(Main.Outer1.Pad);
  Show(Main.Outer2);
  Show(Main.Outer2.Pad);
  Show(Main.Outer2.Home);
  Show(Main.Late);
  Check(Main.Box.Buddy = Main.Outer2.Home.StreetEdit, 'three parts, fixed up after the root''s end');
  Check(Main.Outer1.Buddy = Main.Outer1.Home.StreetEdit, 'scope-relative in the instance''s lines');
  Check(Main.Outer2.Buddy = Main.Outer2.Home.StreetEdit, 'scope-relative names the own instance');
  Check(Main.Outer2.Pad.Buddy2 = Main.Outer1.Home.StreetEdit, 'three parts from another instance''s lines');
  Check(Main.Outer2.Pad.Buddy = Main.Outer2.Home.StreetEdit, 'the frame''s own file, per instance');
  Check(Main.Late.Buddy = Main.Outer1.Home.StreetEdit, 'four parts through the root''s name');
  Main.Free;

  Section('an unresolved part');
  Main := TMainRoot.Create(nil);
  Msg := '';
  try
    ReadComponentFromText(Unresolved, Main, 'Main.form');
  except
    on E: EReadError do
      Msg := E.Message;
  end;
  CheckEqS('Main.form(3,13): error F0105: Component Outer1.Home.Nope not found', Msg, 'F0105 names the whole reference');
  Main.Free;
  Done;
end.
