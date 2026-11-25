// phase: 8
// covers: RTL-1094, RTL-1095, RTL-1096, RTL-1098, RTL-1100, RTL-1101, RTL-1103
program Cls_ReadInlineErrors;
{ Errors of frame instances (task R-242, ui-framework/phase8/frames.md RTL-1094...1096, 1098, 1100): F0109 for an
  override block that names no component of its scope or one of another class, F0110 for an object added inside an
  instance or inside an override block, F0111 for a frame that contains itself directly or through a second frame;
  each is the exact EReadError line, OnError is not consulted; after every failure nothing of the failed read is
  left in the owner, nothing leaks, and a valid text reads afterwards. "inherited" outside an instance and an
  "inline" root stay F0107. }
uses
  SysUtils, Classes, TestKit;

var
  Live: Integer;
  Log: string;

type
  TNode = class(TComponent)
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  end;

  TPart = class(TNode)
  private
    FCaption: string;
  published
    property Caption: string read FCaption write FCaption;
  end;

  TOther = class(TNode)
  end;

  TTestBase = class(TNode)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TTestFrame = class(TTestBase)
  published
    Title: TPart;
    Box: TPart;
  end;

  TSelfFrame = class(TTestBase)
  published
    Pad: TPart;
    Again: TSelfFrame;
  end;

  TCycleB = class;

  TCycleA = class(TTestBase)
  published
    B1: TCycleB;
  end;

  TCycleB = class(TTestBase)
  published
    Pad: TPart;
    A1: TCycleA;
  end;

  TMainRoot = class(TComponent)
  published
    F: TTestFrame;
    S: TSelfFrame;
    A: TCycleA;
    P: TPart;
  end;

  THandler = class
    procedure HandleAll(Reader: TReader; const Message: string; var Handled: Boolean);
  end;

{$FORM Cls_ReadInlineErrorsFrame.form}
{$FORM Cls_ReadInlineErrorsSelf.form}
{$FORM Cls_ReadInlineErrorsCycleA.form}
{$FORM Cls_ReadInlineErrorsCycleB.form}

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

constructor TTestBase.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  InitInheritedComponent(Self, TTestBase, AOwner = nil);
end;

procedure THandler.HandleAll(Reader: TReader; const Message: string; var Handled: Boolean);
begin
  Log := Log + Message + #10;
  Handled := True;
end;

var
  Main: TMainRoot;
  Kept: TPart;
  H: THandler;

{ reads "object Main: TMainRoot" + Body + "end" into Main; '' or the exception's class and message }
function Load(const Body: string; WithHandler: Boolean): string;
var
  Stream: TStringStream;
  R: TReader;
begin
  Result := '';
  Stream := TStringStream.Create('object Main: TMainRoot'#10 + Body + 'end'#10);
  R := TReader.Create(Stream, 4096);
  R.SourceName := 'E.form';
  if WithHandler then
    R.OnError := H.HandleAll;
  try
    try
      R.ReadRootComponent(Main);
    except
      on E: Exception do
        Result := E.ClassName + ': ' + E.Message;
    end;
  finally
    R.Free;
    Stream.Free;
  end;
end;

{ a failed read leaves the owner as it was: only Kept, nothing leaked, no loading flags }
procedure CheckClean(const What: string);
begin
  CheckEqI(1, Main.ComponentCount, What + ': only the kept component in the owner');
  CheckEqI(1, Live, What + ': nothing leaked');
  Check(not (csLoading in Main.ComponentState) and not (csReading in Main.ComponentState),
    What + ': root flags cleared');
end;

{ constructs a frame without an owner; '' or the exception's class and message }
function Construct(AClass: TComponentClass): string;
var
  C: TComponent;
begin
  Result := '';
  try
    C := AClass.Create(nil);
    C.Free;
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

const
  Valid =
    '  inline F: TTestFrame'#10 +
    '    inherited Title: TPart'#10 +
    '      Caption = ''ok'''#10 +
    '    end'#10 +
    '  end'#10;

begin
  RegisterClasses([TPart, TOther, TTestFrame, TSelfFrame, TCycleA, TCycleB, TMainRoot]);
  H := THandler.Create;
  Main := TMainRoot.Create(nil);
  Kept := TPart.Create(Main);
  Kept.Name := 'Kept';
  CheckEqI(1, Live, 'the kept component');

  Section('F0109 (RTL-1095)');
  CheckEqS('EReadError: E.form(3,15): error F0109: Component Nope: TPart is not part of TTestFrame',
    Load('  inline F: TTestFrame'#10'    inherited Nope: TPart'#10'    end'#10'  end'#10, False), 'unknown name');
  CheckClean('unknown name');
  CheckEqS('EReadError: E.form(3,15): error F0109: Component Title: TOther is not part of TTestFrame',
    Load('  inline F: TTestFrame'#10'    inherited Title: TOther'#10'    end'#10'  end'#10, False), 'wrong class');
  CheckClean('wrong class');
  CheckEqS('EReadError: E.form(3,15): error F0109: Component Kept: TPart is not part of TTestFrame',
    Load('  inline F: TTestFrame'#10'    inherited Kept: TPart'#10'    end'#10'  end'#10, False),
    'a component of the root is not part of the frame');
  CheckClean('root component');

  Section('F0110 (RTL-1094)');
  CheckEqS('EReadError: E.form(3,5): error F0110: Components cannot be added to frame instance F',
    Load('  inline F: TTestFrame'#10'    object Extra: TPart'#10'    end'#10'  end'#10, False), 'inside an instance');
  CheckClean('inside an instance');
  CheckEqS('EReadError: E.form(4,7): error F0110: Components cannot be added to frame instance F',
    Load('  inline F: TTestFrame'#10'    inherited Box: TPart'#10'      object Extra: TPart'#10'      end'#10 +
    '    end'#10'  end'#10, False), 'inside an override block');
  CheckClean('inside an override block');
  CheckEqS('EReadError: E.form(3,5): error F0110: Components cannot be added to frame instance F',
    Load('  inline F: TTestFrame'#10'    inline G: TTestFrame'#10'    end'#10'  end'#10, False),
    'an instance inside an instance');
  CheckClean('instance inside an instance');

  Section('OnError cannot skip them');
  Log := '';
  CheckEqS('EReadError: E.form(3,15): error F0109: Component Nope: TPart is not part of TTestFrame',
    Load('  inline F: TTestFrame'#10'    inherited Nope: TPart'#10'    end'#10'  end'#10, True), 'F0109');
  CheckEqS('EReadError: E.form(3,5): error F0110: Components cannot be added to frame instance F',
    Load('  inline F: TTestFrame'#10'    object Extra: TPart'#10'    end'#10'  end'#10, True), 'F0110');
  CheckEqS('EReadError: TSelfFrame.form(4,3): error F0111: Frame TSelfFrame contains itself',
    Load('  inline S: TSelfFrame'#10'  end'#10, True), 'F0111');
  CheckEqS('', Log, 'OnError not consulted');
  CheckClean('with a handler');

  Section('F0111 (RTL-1096)');
  CheckEqS('EReadError: TSelfFrame.form(4,3): error F0111: Frame TSelfFrame contains itself', Construct(TSelfFrame),
    'a frame whose resource holds an instance of itself');
  CheckEqI(1, Live, 'self: nothing leaked');
  CheckEqS('EReadError: TSelfFrame.form(4,3): error F0111: Frame TSelfFrame contains itself',
    Load('  inline S: TSelfFrame'#10'  end'#10, False), 'the same frame as an instance');
  CheckClean('self as an instance');
  CheckEqS('EReadError: TCycleB.form(4,3): error F0111: Frame TCycleA contains itself', Construct(TCycleA),
    'a two-frame cycle');
  CheckEqI(1, Live, 'cycle: nothing leaked');
  CheckEqS('EReadError: TCycleA.form(2,3): error F0111: Frame TCycleB contains itself', Construct(TCycleB),
    'the cycle entered from the other frame');
  CheckEqS('EReadError: TCycleB.form(4,3): error F0111: Frame TCycleA contains itself',
    Load('  inline A: TCycleA'#10'  end'#10, False), 'the cycle as an instance');
  CheckClean('cycle as an instance');

  Section('a valid read after the failures');
  CheckEqS('', Load(Valid, False), 'read');
  Check(Main.F <> nil, 'instance created');
  CheckEqS('ok', Main.F.Title.Caption, 'override applied');
  CheckEqB(True, csInline in Main.F.ComponentState, 'csInline');
  Main.F.Free;
  CheckEqI(1, Live, 'freed');

  Section('F0107 stays');
  CheckEqS('EReadError: E.form(2,3): error F0107: "inherited" objects are not supported',
    Load('  inherited Kept: TPart'#10'  end'#10, False), 'inherited outside an instance');
  CheckEqS('EReadError: E.form(3,5): error F0107: "inherited" objects are not supported',
    Load('  object P: TPart'#10'    inherited Kept: TPart'#10'    end'#10'  end'#10, False),
    'inherited inside an ordinary object');
  CheckClean('F0107');
  Log := '';
  try
    ReadComponentFromText('inline Main: TMainRoot'#10'end'#10, Main, 'Root.form');
  except
    on E: EReadError do
      Log := E.Message;
  end;
  CheckEqS('Root.form(1,1): error F0107: "inline" objects are not supported', Log, 'inline root');
  Main.Free;
  H.Free;
  CheckEqI(0, Live, 'nothing leaked at the end');
  Done;
end.
