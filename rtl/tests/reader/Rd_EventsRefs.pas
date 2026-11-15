// phase: 4
// covers: RTL-496, RTL-498
program Rd_EventsRefs;
{ Events and component references (classes.md RTL-496, RTL-498): an event identifier is bound through
  Root.MethodAddress with Data = Root, OnFindMethod may supply or replace the address, a missing method is F0104
  (EMethodNotFound); a reference is resolved at once when the component exists (also the lookup root itself),
  otherwise recorded and fixed up after the root's end; a qualified reference A.B goes through FindGlobalComponent;
  OnReferenceName may rename; nil clears; a component of the wrong class is F0103, an unresolved one F0105. }
uses
  SysUtils, Classes, TestKit;

var
  Log: string;

type
  TEvRoot = class(TComponent)
  published
    procedure Clicked(Sender: TObject);
    procedure Other(Sender: TObject);
  end;

  TEvComp = class(TComponent)
  private
    FOnClick: TNotifyEvent;
    FBuddy: TEvComp;
    FAnything: TComponent;
  published
    property OnClick: TNotifyEvent read FOnClick write FOnClick;
    property Buddy: TEvComp read FBuddy write FBuddy;
    property Anything: TComponent read FAnything write FAnything;
  end;

  THooks = class
    procedure FindMethod(Reader: TReader; const MethodName: string; var Address: Pointer; var Error: Boolean);
    procedure RefName(Reader: TReader; var Name: string);
  end;

procedure TEvRoot.Clicked(Sender: TObject);
begin
  Log := Log + Name + '.Clicked(' + TComponent(Sender).Name + ');';
end;

procedure TEvRoot.Other(Sender: TObject);
begin
  Log := Log + Name + '.Other(' + TComponent(Sender).Name + ');';
end;

procedure THooks.FindMethod(Reader: TReader; const MethodName: string; var Address: Pointer; var Error: Boolean);
begin
  Log := Log + 'find ' + MethodName + ' ' + BoolToStr(Error, True) + ';';
  if MethodName = 'Alias' then
  begin
    Address := Reader.Root.MethodAddress('Other');
    Error := False;
  end;
end;

procedure THooks.RefName(Reader: TReader; var Name: string);
begin
  Log := Log + 'ref ' + Name + ';';
  if Name = 'Bee' then
    Name := 'B';
end;

var
  Other1, Thing: TComponent;

function FindGlobal(const Name: string): TComponent;
begin
  if SameText(Name, 'Other1') then
    Result := Other1
  else
    Result := nil;
end;

function Err(const Body: string): string;
var
  Q: TEvRoot;
begin
  Result := '';
  Q := TEvRoot.Create(nil);
  try
    ReadComponentFromText('object Root1: TEvRoot'#10 + Body + 'end'#10, Q, 'E.form');
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
  Q.Free;
end;

var
  Root: TEvRoot;
  A, B: TEvComp;
  H: THooks;
  S: TStringStream;
  R: TReader;
begin
  RegisterClasses([TEvComp, TComponent]);
  Other1 := TComponent.Create(nil);
  Other1.Name := 'Other1';
  Thing := TComponent.Create(Other1);
  Thing.Name := 'Thing';
  FindGlobalComponent := FindGlobal;

  Section('events and references');
  Root := TEvRoot.Create(nil);
  ReadComponentFromText(
    'object Root1: TEvRoot'#10 +
    '  object A: TEvComp'#10 +
    '    OnClick = Clicked'#10 +
    '    Buddy = B'#10 +
    '    Anything = root1'#10 +
    '  end'#10 +
    '  object B: TEvComp'#10 +
    '    OnClick = other'#10 +
    '    Buddy = A'#10 +
    '    Anything = Other1.Thing'#10 +
    '  end'#10 +
    'end'#10, Root, 'E.form');
  A := TEvComp(Root.FindComponent('A'));
  B := TEvComp(Root.FindComponent('B'));
  A.OnClick(A);
  B.OnClick(B);
  CheckEqS('Root1.Clicked(A);Root1.Other(B);', Log, 'events bound to the root''s methods');
  Check(TMethod(A.OnClick).Data = Pointer(Root), 'event data is the root');
  Check(A.Buddy = B, 'forward reference fixed up');
  Check(B.Buddy = A, 'backward reference');
  Check(A.Anything = Root, 'the lookup root by name');
  Check(B.Anything = Thing, 'qualified reference through FindGlobalComponent');
  Root.Free;
  Root := TEvRoot.Create(nil);
  ReadComponentFromText('object Root1: TEvRoot'#10'  object A: TEvComp'#10'    Buddy = nil'#10'  end'#10'end'#10, Root,
    'E.form');
  Check(TEvComp(Root.FindComponent('A')).Buddy = nil, 'nil clears a reference');
  Root.Free;

  Section('hooks');
  Root := TEvRoot.Create(nil);
  H := THooks.Create;
  S := TStringStream.Create(
    'object Root1: TEvRoot'#10 +
    '  object A: TEvComp'#10 +
    '    OnClick = Alias'#10 +
    '    Buddy = Bee'#10 +
    '  end'#10 +
    '  object B: TEvComp'#10 +
    '    OnClick = Clicked'#10 +
    '  end'#10 +
    'end'#10);
  R := TReader.Create(S, 4096);
  R.OnFindMethod := H.FindMethod;
  R.OnReferenceName := H.RefName;
  Log := '';
  R.ReadRootComponent(Root);
  CheckEqS('find Alias True;ref Bee;find Clicked False;', Log, 'OnFindMethod and OnReferenceName');
  A := TEvComp(Root.FindComponent('A'));
  Log := '';
  A.OnClick(A);
  CheckEqS('Root1.Other(A);', Log, 'address supplied by OnFindMethod');
  Check(A.Buddy = Root.FindComponent('B'), 'renamed reference');
  R.Free;
  S.Free;
  H.Free;
  Root.Free;

  Section('errors');
  CheckEqS('EMethodNotFound: E.form(3,15): error F0104: Method Missing not found in TEvRoot',
    Err('  object A: TEvComp'#10'    OnClick = Missing'#10'  end'#10), 'missing method');
  CheckEqS('EReadError: E.form(3,13): error F0103: Invalid value for property Buddy',
    Err('  object A: TEvComp'#10'    Buddy = Root1'#10'  end'#10), 'wrong class now');
  CheckEqS('EReadError: E.form(3,13): error F0103: Invalid value for property Buddy',
    Err('  object A: TEvComp'#10'    Buddy = Later'#10'  end'#10'  object Later: TComponent'#10'  end'#10),
    'wrong class at fix-up time');
  CheckEqS('EReadError: E.form(3,13): error F0105: Component Ghost not found',
    Err('  object A: TEvComp'#10'    Buddy = Ghost'#10'  end'#10), 'unresolved reference');
  CheckEqS('EReadError: E.form(3,16): error F0105: Component Other1.Nothing not found',
    Err('  object A: TEvComp'#10'    Anything = Other1.Nothing'#10'  end'#10), 'unresolved qualified reference');
  FindGlobalComponent := nil;
  CheckEqS('EReadError: E.form(3,16): error F0105: Component Other1.Thing not found',
    Err('  object A: TEvComp'#10'    Anything = Other1.Thing'#10'  end'#10), 'qualified reference without the hook');
  CheckEqS('EReadError: E.form(3,15): error F0103: Invalid value for property OnClick',
    Err('  object A: TEvComp'#10'    OnClick = ''x'''#10'  end'#10), 'string for an event');
  Other1.Free;
  Done;
end.
