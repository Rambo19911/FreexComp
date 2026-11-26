// phase: 8
// covers: RTL-1104
program Cls_ReadInlineSelfRef;
{ The header name inside a frame's own form file (task R-245, RTL-1104): a frame read with an owner keeps an empty
  name (RTL-1099), yet a reference in its file to the header name, plain or qualified (Header.Child), resolves to the
  instance being read and its own children, now or through a fix-up, for every instance on its own; the header name
  is tried before the FindGlobalComponent hook, so a global component of the same name is not used. In another file
  the frame's header name means nothing: a plain one is F0105, a qualified one goes through the hook as before. }
uses
  SysUtils, Classes, TestKit;

type
  TNode = class(TComponent)
  private
    FBuddy: TComponent;
  published
    property Buddy: TComponent read FBuddy write FBuddy;
  end;

  TPart = class(TNode)
  end;

  { stands for TFrame: a class without a resource of its own }
  TTestBase = class(TNode)
  end;

  TFooterFrame = class(TTestBase)
  published
    Title: TPart;
    Note: TPart;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TMainRoot = class(TComponent)
  published
    F1: TFooterFrame;
    F2: TFooterFrame;
    Other: TPart;
  end;

{$FORM Cls_ReadInlineSelfRefFrame.form}

var
  Decoy, DecoyTitle: TComponent;

constructor TFooterFrame.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  InitInheritedComponent(Self, TTestBase, AOwner = nil);
end;

{ the FindGlobalComponent hook: knows one global component, named like the frame's header }
function FindGlobal(const Name: string): TComponent;
begin
  if CompareText(Name, Decoy.Name) = 0 then
    Result := Decoy
  else
    Result := nil;
end;

function NameOf(C: TComponent): string;
begin
  if C = nil then
    Result := 'nil'
  else if C = Decoy then
    Result := 'decoy'
  else if C.Name = '' then
    Result := '<' + C.ClassName + '>'
  else
    Result := C.Name;
end;

{ the frame's own references: true when all three point into F itself }
procedure CheckOwn(F: TFooterFrame; const What: string);
begin
  Check(F.Title.Buddy = F, What + ': plain header name is the instance');
  Check(F.Note.Buddy = F.Title, What + ': qualified header name reaches its own child');
  Check(F.Buddy = F.Note, What + ': qualified forward reference fixed up in the instance');
  WriteLn(What, ': Title.Buddy=', NameOf(F.Title.Buddy), ' Note.Buddy=', NameOf(F.Note.Buddy), ' Buddy=',
    NameOf(F.Buddy));
end;

{ reads "object Main: TMainRoot" + Body + "end" into a new root; '' or the exception's class and message }
function Load(const Body: string): string;
var
  Main: TMainRoot;
begin
  Result := '';
  Main := TMainRoot.Create(nil);
  try
    try
      ReadComponentFromText('object Main: TMainRoot'#10 + Body + 'end'#10, Main, 'E.form');
      Result := 'read';
      if (Main.F1 <> nil) and (Main.F1.Title.Buddy = DecoyTitle) then
        Result := 'read, reference into the global component';
    except
      on E: Exception do
        Result := E.ClassName + ': ' + E.Message;
    end;
  finally
    Main.Free;
  end;
end;

var
  Main: TMainRoot;
  Alone, X, Y: TFooterFrame;
begin
  RegisterClasses([TPart, TFooterFrame, TMainRoot]);
  Decoy := TComponent.Create(nil);
  Decoy.Name := 'FooterFrame';
  DecoyTitle := TPart.Create(Decoy);
  DecoyTitle.Name := 'Title';
  FindGlobalComponent := FindGlobal;

  Section('a frame without an owner: the header name is also its name');
  Alone := TFooterFrame.Create(nil);
  CheckEqS('FooterFrame', Alone.Name, 'root name applied');
  CheckOwn(Alone, 'alone');
  Alone.Free;

  Section('two instances in a form: each file reference names its own instance');
  Main := TMainRoot.Create(nil);
  ReadComponentFromText(
    'object Main: TMainRoot'#10 +
    '  inline F1: TFooterFrame'#10 +
    '  end'#10 +
    '  inline F2: TFooterFrame'#10 +
    '  end'#10 +
    'end'#10, Main, 'Main.form');
  CheckEqS('F1|F2', Main.F1.Name + '|' + Main.F2.Name, 'instance names from the form');
  CheckOwn(Main.F1, 'F1');
  CheckOwn(Main.F2, 'F2');
  Check(Main.F1.Note.Buddy <> DecoyTitle, 'the global component of the same name is not used');

  Section('frames created in code with an owner');
  X := TFooterFrame.Create(Main);
  Y := TFooterFrame.Create(Main);
  CheckEqS('|', X.Name + '|' + Y.Name, 'both keep an empty name');
  CheckOwn(X, 'X');
  CheckOwn(Y, 'Y');
  Main.Free;

  Section('in another file the frame''s header name is not a name of that file');
  CheckEqS('EReadError: E.form(4,15): error F0105: Component FooterFrame not found', Load(
    '  inline F1: TFooterFrame'#10 +
    '    inherited Title: TPart'#10 +
    '      Buddy = FooterFrame'#10 +
    '    end'#10 +
    '  end'#10), 'plain header name of the frame in the form');
  CheckEqS('read, reference into the global component', Load(
    '  inline F1: TFooterFrame'#10 +
    '    inherited Title: TPart'#10 +
    '      Buddy = FooterFrame.Title'#10 +
    '    end'#10 +
    '  end'#10), 'qualified one goes through FindGlobalComponent');

  FindGlobalComponent := nil;
  Decoy.Free;
  Done;
end.
