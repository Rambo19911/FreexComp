{ ReadFieldClassKit - the body of classes/Cls_ReadFieldClass and classes/Cls_ReadFieldClassPublished (task R-246,
  RTL-1105): no class of this unit is registered, so every object and inline line is resolved from the published
  fields of the class chain of the root of the file being read. The same checks run with the default broad RTTI scope
  and with --rtti published and print the same lines: fields that are not published (in the broad tables only) are
  never used. }
unit ReadFieldClassKit;

interface

procedure Run;

implementation

uses
  SysUtils, Classes, ReadFieldClassOther, TestKit;

type
  TNode = class(TComponent)
  private
    FCaption: string;
  public
    ParentComp: TComponent;
    procedure SetParentComponent(Value: TComponent); override;
  published
    property Caption: string read FCaption write FCaption;
  end;

  TPart = class(TNode)
  end;

  { declared, but no field has this class }
  TStray = class(TNode)
  end;

  { only in fields that are not published }
  THidden = class(TNode)
  end;

  TPublicOnly = class(TNode)
  end;

  { the class of a published field that is no component }
  TInfo = class(TPersistent)
  end;

  TBaseOnly = class(TNode)
  end;

  { stands for TFrame: a class without a resource of its own }
  TTestBase = class(TNode)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TInnerFrame = class(TTestBase)
  published
    Lbl: TPart;
  end;

  TOuterFrame = class(TTestBase)
  published
    Pad: TPart;
    Sub: TInnerFrame;
  end;

  TBaseRoot = class(TComponent)
  published
    BaseItem: TBaseOnly;
  end;

  TMainRoot = class(TBaseRoot)
  protected
    Hidden: THidden;
  public
    PubPart: TPublicOnly;
  published
    Alt: ReadFieldClassOther.TPart;
    Frame1: TOuterFrame;
    Edit1: TPart;
    Edit2: TPart;
    Info: TInfo;
  end;

{$FORM ReadFieldClassInner.form}
{$FORM ReadFieldClassOuter.form}

procedure TNode.SetParentComponent(Value: TComponent);
begin
  ParentComp := Value;
end;

constructor TTestBase.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  InitInheritedComponent(Self, TTestBase, AOwner = nil);
end;

function NameOf(C: TComponent): string;
begin
  if C = nil then
    Result := 'nil'
  else if C.Name = '' then
    Result := '<' + C.ClassName + '>'
  else
    Result := C.Name;
end;

procedure Dump(C: TComponent; const Indent: string);
var
  I: Integer;
  Line: string;
begin
  Line := Indent + NameOf(C) + ': ' + C.ClassName + ' unit=' + C.UnitName + ' owner=' + NameOf(C.Owner);
  if C is TNode then
    Line := Line + ' parent=' + NameOf(TNode(C).ParentComp) + ' caption=' + QuoteStr(TNode(C).Caption);
  if csInline in C.ComponentState then
    Line := Line + ' inline';
  WriteLn(Line);
  for I := 0 to C.ComponentCount - 1 do
    Dump(C.Components[I], Indent + '  ');
end;

const
  MainText =
    'object Main: TMainRoot'#10 +
    '  inline Frame1: TOuterFrame'#10 +
    '    inherited Sub: TInnerFrame'#10 +
    '      inherited Lbl: TPart'#10 +
    '        Caption = ''set by the form'''#10 +
    '      end'#10 +
    '    end'#10 +
    '  end'#10 +
    '  object Edit1: TPart'#10 +
    '    Caption = ''edit1'''#10 +
    '  end'#10 +
    '  object edit2: tpart'#10 +
    '  end'#10 +
    '  object Zed: TPart'#10 +
    '  end'#10 +
    '  object BaseItem: TBaseOnly'#10 +
    '  end'#10 +
    'end'#10;

  FrameThenStray =
    '  inline Frame1: TOuterFrame'#10 +
    '    inherited Sub: TInnerFrame'#10 +
    '      inherited Lbl: TPart'#10 +
    '      end'#10 +
    '    end'#10 +
    '  end'#10 +
    '  object Lbl2: TStray'#10 +
    '  end'#10;

procedure ReadError(const Body, Expected, What: string);
var
  Main: TMainRoot;
  Cls, Msg: string;
begin
  Main := TMainRoot.Create(nil);
  Cls := '';
  Msg := '';
  try
    ReadComponentFromText('object Main: TMainRoot'#10 + Body + 'end'#10, Main, 'Main.form');
  except
    on E: Exception do
    begin
      Cls := E.ClassName;
      Msg := E.Message;
    end;
  end;
  CheckEqS('EClassNotFound', Cls, What + ' (class)');
  CheckEqS(Expected, Msg, What);
  CheckEqI(0, Main.ComponentCount, What + ': nothing left in the owner');
  Main.Free;
end;

procedure Run;
var
  Main: TMainRoot;
  Frame: TOuterFrame;
begin
  Check(GetClass('TPart') = nil, 'TPart is not registered');
  Check(GetClass('TOuterFrame') = nil, 'TOuterFrame is not registered');
  Check(GetClass('TInnerFrame') = nil, 'TInnerFrame is not registered');

  Section('a frame created in code: its file, and the nested frame through the frame''s own fields');
  Frame := TOuterFrame.Create(nil);
  Dump(Frame, '');
  Frame.Free;

  Section('a form: a frame instance, components and an ancestor''s field, none registered');
  Main := TMainRoot.Create(nil);
  ReadComponentFromText(MainText, Main, 'Main.form');
  Dump(Main, '');
  Check(Main.Frame1.Sub.Lbl.ParentComp = Main.Frame1.Sub, 'the nested frame keeps its child');
  Check(Main.Edit1.ClassType = TPart, 'Edit1: the class of the field of that name');
  Check(Main.Edit2.ClassType = TPart, 'edit2: field and class names compared case-insensitively');
  Check(Main.BaseItem.ClassType = TBaseOnly, 'BaseItem: a field of an ancestor of the root');
  Check(Main.FindComponent('Zed').ClassType = ReadFieldClassOther.TPart,
    'Zed: no field of that name, the first field whose class has the name');
  Main.Free;

  Section('no published field: the error as before');
  ReadError('  object Edit1: TStray'#10'  end'#10,
    'Main.form(2,17): error F0101: Class TStray not found', 'a class in no field');
  ReadError('  object Hidden: THidden'#10'  end'#10,
    'Main.form(2,18): error F0101: Class THidden not found', 'a protected field');
  ReadError('  object PubPart: TPublicOnly'#10'  end'#10,
    'Main.form(2,19): error F0101: Class TPublicOnly not found', 'a public field');
  ReadError('  object Info: TInfo'#10'  end'#10,
    'Main.form(2,16): error F0101: Class TInfo not found', 'a published field whose class is no component');
  ReadError(FrameThenStray, 'Main.form(8,16): error F0101: Class TStray not found', 'after a frame instance');

  Section('GetClass stays first');
  RegisterClass(ReadFieldClassOther.TPart);
  Main := TMainRoot.Create(nil);
  ReadComponentFromText('object Main: TMainRoot'#10'  object Edit1: TPart'#10'  end'#10'end'#10, Main, 'Main.form');
  Check(Main.FindComponent('Edit1').ClassType = ReadFieldClassOther.TPart, 'the registered class, not the class of the field');
  Main.Free;
  UnRegisterClass(ReadFieldClassOther.TPart);
  Done;
end;

end.
