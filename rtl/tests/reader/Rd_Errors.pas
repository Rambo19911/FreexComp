// phase: 4
// covers: RTL-495, RTL-497, RTL-500
program Rd_Errors;
{ Reader errors (classes.md RTL-500): every code with its exception class (all EReadError), exact message
  "<SourceName>(<line>,<col>): error <code>: <text>" and position; on failure every component the reader created is
  destroyed, components the root already owned stay, the root's csLoading/csReading are cleared (RTL-495 step 7);
  nested inherited/inline objects and an inline root are F0107 (RTL-497); OnError may skip a property line for
  F0102 ... F0105 (the whole value, also a multi-line one) but is not consulted for F0001, F0101, F0106, F0107. }
uses
  SysUtils, Classes, TestKit;

var
  Live: Integer;
  Log: string;

type
  TCounted = class(TComponent)
  private
    FCount: Integer;
    FOnGo: TNotifyEvent;
    FBuddy: TCounted;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  published
    property Count: Integer read FCount write FCount;
    property OnGo: TNotifyEvent read FOnGo write FOnGo;
    property Buddy: TCounted read FBuddy write FBuddy;
  end;

  TNotComponent = class(TPersistent)
  end;

  THandler = class
    procedure HandleAll(Reader: TReader; const Message: string; var Handled: Boolean);
  end;

constructor TCounted.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  Live := Live + 1;
end;

destructor TCounted.Destroy;
begin
  Live := Live - 1;
  inherited Destroy;
end;

procedure THandler.HandleAll(Reader: TReader; const Message: string; var Handled: Boolean);
begin
  Log := Log + Message + #10;
  Handled := True;
end;

var
  Root: TCounted;
  Kept: TCounted;
  H: THandler;
  Msg: string;

function Load(const Body: string; WithHandler: Boolean): string;
var
  S: TStringStream;
  R: TReader;
begin
  Result := '';
  S := TStringStream.Create('object Root1: TCounted'#10 + Body + 'end'#10);
  R := TReader.Create(S, 4096);
  R.SourceName := 'Err.form';
  if WithHandler then
    R.OnError := H.HandleAll;
  try
    try
      R.ReadRootComponent(Root);
    except
      on E: EReadError do
        Result := E.ClassName + ': ' + E.Message;
    end;
  finally
    R.Free;
    S.Free;
  end;
end;

function Err(const Body: string): string;
begin
  Result := Load(Body, False);
end;

begin
  RegisterClasses([TCounted, TNotComponent]);
  H := THandler.Create;
  Root := TCounted.Create(nil);
  Kept := TCounted.Create(Root);
  Kept.Name := 'Kept';

  Section('codes (RTL-500)');
  CheckEqS('EReadError: Err.form(2,9): error F0001: Syntax error: invalid character', Err('  Count @'#10), 'F0001');
  CheckEqS('EClassNotFound: Err.form(2,13): error F0101: Class TNope not found',
    Err('  object A: TNope'#10'  end'#10), 'F0101 unknown class');
  CheckEqS('EClassNotFound: Err.form(2,13): error F0101: Class TNotComponent not found',
    Err('  object A: TNotComponent'#10'  end'#10), 'F0101 registered class that is not a component');
  CheckEqS('EReadError: Err.form(2,3): error F0102: Property Nope does not exist on TCounted', Err('  Nope = 1'#10),
    'F0102');
  CheckEqS('EReadError: Err.form(2,11): error F0103: Invalid value for property Count', Err('  Count = x'#10),
    'F0103');
  CheckEqS('EMethodNotFound: Err.form(2,10): error F0104: Method Go not found in TCounted', Err('  OnGo = Go'#10),
    'F0104');
  CheckEqS('EReadError: Err.form(2,11): error F0105: Component Ghost not found', Err('  Buddy = Ghost'#10), 'F0105');
  CheckEqS('EReadError: Err.form(4,10): error F0106: A component named a already exists',
    Err('  object A: TCounted'#10'  end'#10'  object a: TCounted'#10'  end'#10), 'F0106 duplicate');
  CheckEqS('EReadError: Err.form(2,10): error F0106: A component named Kept already exists',
    Err('  object Kept: TCounted'#10'  end'#10), 'F0106 against an existing component');
  CheckEqS('EReadError: Err.form(3,3): error F0107: "inherited" objects are not supported',
    Err('  object A: TCounted'#10'  inherited B: TCounted'#10'  end'#10'  end'#10), 'F0107 inherited');
  CheckEqS('EReadError: Err.form(2,3): error F0107: "inline" objects are not supported',
    Err('  INLINE B: TCounted'#10'  end'#10), 'F0107 inline');
  Check(EClassNotFound.InheritsFrom(EReadError), 'EClassNotFound is an EReadError');
  Check(EMethodNotFound.InheritsFrom(EReadError), 'EMethodNotFound is an EReadError');

  Section('inline root');
  Msg := '';
  try
    ReadComponentFromText('inline Root1: TCounted'#10'end'#10, Root, 'In.form');
  except
    on E: EReadError do
      Msg := E.Message;
  end;
  CheckEqS('In.form(1,1): error F0107: "inline" objects are not supported', Msg, 'F0107 inline root');

  Section('cleanup (RTL-495 step 7)');
  CheckEqI(2, Live, 'root and kept component before');
  CheckEqS('EReadError: Err.form(7,13): error F0103: Invalid value for property Count',
    Err('  object A: TCounted'#10'    object A1: TCounted'#10'    end'#10'  end'#10 +
    '  object B: TCounted'#10'    Count = x'#10'  end'#10), 'failure in the third component');
  CheckEqI(2, Live, 'every created component destroyed');
  CheckEqI(1, Root.ComponentCount, 'the existing component stays');
  Check(Root.Components[0] = Kept, 'it is the kept one');
  Check(not (csLoading in Root.ComponentState), 'csLoading cleared');
  Check(not (csReading in Root.ComponentState), 'csReading cleared');
  CheckEqS('EClassNotFound: Err.form(4,13): error F0101: Class TNope not found',
    Err('  object A: TCounted'#10'  end'#10'  object B: TNope'#10'  end'#10), 'unknown class after a child');
  CheckEqI(2, Live, 'cleaned up after F0101');

  Section('OnError');
  Log := '';
  CheckEqS('', Load(
    '  Nope = ('#10'    ''a'''#10'    ''b'''#10'  )'#10 +
    '  Count = 7'#10 +
    '  object A: TCounted'#10 +
    '    Count = <'#10'      item'#10'      end'#10'    >'#10 +
    '    OnGo = Missing'#10 +
    '    Buddy = Ghost'#10 +
    '    Count = 3'#10 +
    '  end'#10, True), 'every handled error is skipped');
  CheckEqS(
    'Err.form(2,3): error F0102: Property Nope does not exist on TCounted'#10 +
    'Err.form(8,13): error F0103: Invalid value for property Count'#10 +
    'Err.form(12,12): error F0104: Method Missing not found in TCounted'#10 +
    'Err.form(13,13): error F0105: Component Ghost not found'#10, Log, 'messages in order');
  CheckEqI(7, Root.Count, 'line after a skipped list applied');
  CheckEqI(3, TCounted(Root.FindComponent('A')).Count, 'line after skipped values applied');
  Check(TCounted(Root.FindComponent('A')).Buddy = nil, 'unresolved reference left alone');
  Root.FindComponent('A').Free;
  Log := '';
  CheckEqS('EClassNotFound: Err.form(2,13): error F0101: Class TNope not found',
    Load('  object A: TNope'#10'  end'#10, True), 'F0101 is not skippable');
  CheckEqS('EReadError: Err.form(2,9): error F0001: Syntax error: invalid character', Load('  Count @'#10, True),
    'F0001 is not skippable');
  CheckEqS('EReadError: Err.form(2,10): error F0106: A component named Kept already exists',
    Load('  object Kept: TCounted'#10'  end'#10, True), 'F0106 is not skippable');
  CheckEqS('EReadError: Err.form(2,3): error F0107: "inline" objects are not supported',
    Load('  inline B: TCounted'#10'  end'#10, True), 'F0107 is not skippable');
  CheckEqS('', Log, 'OnError not consulted for them');
  Root.Free;
  H.Free;
  CheckEqI(0, Live, 'nothing leaked');
  Done;
end.
