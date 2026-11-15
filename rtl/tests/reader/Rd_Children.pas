// phase: 4
// covers: RTL-495, RTL-497, RTL-499, RTL-470
program Rd_Children;
{ Nested objects (classes.md section 10.5): classes found through the registry (RTL-470) or OnFindComponentClass,
  created with the reader's Owner (the root) or supplied by OnCreateComponent; csLoading/csReading set,
  SetParentComponent before any property, OnSetName before the name, ReadState reads the body (RTL-497); every
  property line of a component before its children, children in file order (RTL-499); the root's name before its
  properties; after the root's end csReading is cleared on all, then Loaded runs children before parents and the
  root last (RTL-495); published fields of the root are bound by name. }
uses
  SysUtils, Classes, TestKit;

var
  Log: string;

type
  TNode = class(TComponent)
  private
    FCaption: string;
    FParentComp: TComponent;
    procedure SetCaption(const Value: string);
  protected
    procedure SetParentComponent(Value: TComponent); override;
    procedure ReadState(Reader: TReader); override;
    procedure Loaded; override;
  public
    function GetParentComponent: TComponent; override;
    function HasParent: Boolean; override;
  published
    property Caption: string read FCaption write SetCaption;
  end;

  TSpecial = class(TNode)
  end;

  TRootNode = class(TNode)
  published
    A: TNode;
    B: TNode;
  end;

  THooks = class
    procedure FindClass(Reader: TReader; const ClassName: string; var ComponentClass: TComponentClass);
    procedure CreateComp(Reader: TReader; ComponentClass: TComponentClass; var Component: TComponent);
    procedure SetName(Reader: TReader; Component: TComponent; var Name: string);
  end;

function States(C: TComponent): string;
begin
  Result := '';
  if csLoading in C.ComponentState then
    Result := Result + 'L';
  if csReading in C.ComponentState then
    Result := Result + 'R';
end;

procedure TNode.SetCaption(const Value: string);
begin
  FCaption := Value;
  Log := Log + 'caption ' + Name + '=' + Value + ' ' + States(Self) + ';';
end;

procedure TNode.SetParentComponent(Value: TComponent);
begin
  FParentComp := Value;
  Log := Log + 'parent ' + Value.Name + '>' + Name + ';';
end;

procedure TNode.ReadState(Reader: TReader);
begin
  Log := Log + 'readstate ' + Name + ';';
  inherited ReadState(Reader);
end;

procedure TNode.Loaded;
begin
  Log := Log + 'loaded ' + Name + ' ' + States(Self) + ';';
  inherited Loaded;
end;

function TNode.GetParentComponent: TComponent;
begin
  Result := FParentComp;
end;

function TNode.HasParent: Boolean;
begin
  Result := FParentComp <> nil;
end;

procedure THooks.FindClass(Reader: TReader; const ClassName: string; var ComponentClass: TComponentClass);
begin
  Log := Log + 'find ' + ClassName + ';';
  if ClassName = 'TAlias' then
    ComponentClass := TNode;
end;

procedure THooks.CreateComp(Reader: TReader; ComponentClass: TComponentClass; var Component: TComponent);
begin
  Log := Log + 'create ' + ComponentClass.ClassName + ';';
  if ComponentClass = TSpecial then
    Component := TSpecial.Create(Reader.Owner);
end;

procedure THooks.SetName(Reader: TReader; Component: TComponent; var Name: string);
begin
  Log := Log + 'setname ' + Name + ';';
  if Name = 'Bee' then
    Name := 'B';
end;

const
  Form =
    'object Root1: TRootNode'#10 +
    '  Caption = ''r'''#10 +
    '  object A: TNode'#10 +
    '    Caption = ''a'''#10 +
    '    object A1: TNode'#10 +
    '      Caption = ''a1'''#10 +
    '    end'#10 +
    '  end'#10 +
    '  object B: TNode'#10 +
    '    Caption = ''b'''#10 +
    '  end'#10 +
    'end'#10;

var
  Root: TRootNode;
  H: THooks;
  S: TStringStream;
  R: TReader;
  I: Integer;
  Names: string;
begin
  RegisterClasses([TNode, TSpecial]);

  Section('order (RTL-495, RTL-497, RTL-499)');
  Root := TRootNode.Create(nil);
  ReadComponentFromText(Form, Root, 'Root.form');
  CheckEqS('readstate Root1;caption Root1=r LR;' +
    'parent Root1>;readstate A;caption A=a LR;' +
    'parent A>;readstate A1;caption A1=a1 LR;' +
    'parent Root1>;readstate B;caption B=b LR;' +
    'loaded B L;loaded A1 L;loaded A L;loaded Root1 L;', Log, 'creation, properties and Loaded order');
  Names := '';
  for I := 0 to Root.ComponentCount - 1 do
    Names := Names + Root.Components[I].Name + ' ';
  CheckEqS('A A1 B ', Names, 'every child is owned by the root, in file order');
  Check(Root.FindComponent('A1').GetParentComponent = Root.A, 'nested parent');
  Check(Root.A.GetParentComponent = Root, 'parent of a direct child');
  Check(Root.A.Name = 'A', 'published field A bound');
  Check(Root.B.Name = 'B', 'published field B bound');
  Check(Root.B.ComponentState = [], 'flags cleared on children');
  Check(Root.ComponentState = [], 'flags cleared on the root');
  Root.Free;

  Section('hooks');
  Root := TRootNode.Create(nil);
  H := THooks.Create;
  S := TStringStream.Create(
    'object Root1: TRootNode'#10 +
    '  object A: TAlias'#10 +
    '  end'#10 +
    '  object Bee: TSpecial'#10 +
    '  end'#10 +
    'end'#10);
  R := TReader.Create(S, 4096);
  R.OnFindComponentClass := H.FindClass;
  R.OnCreateComponent := H.CreateComp;
  R.OnSetName := H.SetName;
  Log := '';
  R.ReadRootComponent(Root);
  CheckEqS('setname Root1;readstate Root1;' +
    'find TAlias;create TNode;parent Root1>;setname A;readstate A;' +
    'find TSpecial;create TSpecial;parent Root1>;setname Bee;readstate B;' +
    'loaded B L;loaded A L;loaded Root1 L;', Log, 'OnFindComponentClass, OnCreateComponent, OnSetName');
  Check(Root.A.ClassType = TNode, 'class from OnFindComponentClass');
  Check(Root.B is TSpecial, 'component from OnCreateComponent');
  CheckEqI(2, Root.ComponentCount, 'the supplied component is not created twice');
  Check(R.Owner = Root, 'Owner is the root');
  Check(R.Root = Root, 'Root');
  Check(R.LookupRoot = Root, 'LookupRoot');
  R.Free;
  S.Free;
  H.Free;
  Root.Free;
  Done;
end.
