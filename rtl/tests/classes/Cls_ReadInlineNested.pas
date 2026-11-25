// phase: 8
// covers: RTL-1092
program Cls_ReadInlineNested;
{ Nested frames (task R-242, ui-framework/phase8/frames.md RTL-1092): a frame whose resource holds an instance of a
  second frame; the instance inside the frame has csInline, so it is the scope of the override blocks nested in its
  own override block: the outer file reaches a child of the inner instance through two nested override blocks, the
  frame's own override of that child stays for the instance that does not override it; a name is looked up in the
  innermost scope only (a component of the outer frame is not part of the inner one, F0109). Loaded runs once per
  reader that reached a component. }
uses
  SysUtils, Classes, TestKit;

type
  TNode = class(TComponent)
  private
    FCaption: string;
    FSize: Integer;
  public
    ParentComp: TComponent;
    LoadedCount: Integer;
    procedure SetParentComponent(Value: TComponent); override;
    procedure Loaded; override;
  published
    property Caption: string read FCaption write FCaption;
    property Size: Integer read FSize write FSize;
  end;

  TPart = class(TNode)
  end;

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

  TMainRoot = class(TComponent)
  published
    Outer1: TOuterFrame;
    Outer2: TOuterFrame;
  end;

{$FORM Cls_ReadInlineNestedInner.form}
{$FORM Cls_ReadInlineNestedOuter.form}

procedure TNode.SetParentComponent(Value: TComponent);
begin
  ParentComp := Value;
end;

procedure TNode.Loaded;
begin
  inherited Loaded;
  LoadedCount := LoadedCount + 1;
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
  N: TNode;
  Line: string;
begin
  Line := Indent + NameOf(C) + ': ' + C.ClassName + ' owner=' + NameOf(C.Owner);
  if C is TNode then
  begin
    N := TNode(C);
    Line := Line + ' parent=' + NameOf(N.ParentComp) + ' caption=' + QuoteStr(N.Caption) + ' size=' +
      IntToStr(N.Size) + ' loaded=' + IntToStr(N.LoadedCount);
  end;
  if csInline in C.ComponentState then
    Line := Line + ' inline';
  if csLoading in C.ComponentState then
    Line := Line + ' loading';
  WriteLn(Line);
  for I := 0 to C.ComponentCount - 1 do
    Dump(C.Components[I], Indent + '  ');
end;

const
  MainText =
    'object Main: TMainRoot'#10 +
    '  inline Outer1: TOuterFrame'#10 +
    '    inherited Sub: TInnerFrame'#10 +
    '      Size = 9'#10 +
    '      inherited Lbl: TPart'#10 +
    '        Caption = ''set by the form'''#10 +
    '      end'#10 +
    '    end'#10 +
    '  end'#10 +
    '  inline Outer2: TOuterFrame'#10 +
    '  end'#10 +
    'end'#10;

  WrongScope =
    'object Main: TMainRoot'#10 +
    '  inline Outer1: TOuterFrame'#10 +
    '    inherited Sub: TInnerFrame'#10 +
    '      inherited Pad: TPart'#10 +
    '      end'#10 +
    '    end'#10 +
    '  end'#10 +
    'end'#10;

var
  Main: TMainRoot;
  Msg: string;
begin
  RegisterClasses([TPart, TInnerFrame, TOuterFrame, TMainRoot]);

  Section('a frame with a frame instance, created in code');
  Main := TMainRoot.Create(nil);
  Dump(TOuterFrame.Create(Main), '');
  Main.Free;

  Section('nested override blocks (RTL-1092)');
  Main := TMainRoot.Create(nil);
  ReadComponentFromText(MainText, Main, 'Main.form');
  Dump(Main, '');
  Check(Main.Outer1.Sub.Lbl.ParentComp = Main.Outer1.Sub, 'the inner child keeps its parent');
  Check(Main.Outer1.Sub.ParentComp = Main.Outer1, 'the inner instance keeps its parent');
  Main.Free;

  Section('the innermost scope only');
  Main := TMainRoot.Create(nil);
  Msg := '';
  try
    ReadComponentFromText(WrongScope, Main, 'Main.form');
  except
    on E: EReadError do
      Msg := E.Message;
  end;
  CheckEqS('Main.form(4,17): error F0109: Component Pad: TPart is not part of TInnerFrame', Msg,
    'a component of the outer frame is not part of the inner instance');
  CheckEqI(0, Main.ComponentCount, 'nothing left in the owner');
  Main.Free;
  Done;
end.
