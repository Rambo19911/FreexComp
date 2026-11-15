// phase: 4
// covers: RTL-497, RTL-500
program Cls_ReaderSetParentError;
{ TReader and SetParentComponent (task R-210): the name of a nested object is set before SetParentComponent, so a
  component that refuses its parent can name itself; an EInvalidOperation raised by SetParentComponent becomes
  F0103 with the exception's text at the position of the object's name. OnError may handle it: the component
  stays without a parent and reading continues. }
uses
  SysUtils, Classes, TestKit;

type
  TBox = class(TComponent)
  end;

  TPlain = class(TComponent)
  end;

  TChild = class(TComponent)
  public
    NameAtParent: string;
    ParentComp: TComponent;
    Width: Integer;
    procedure SetParentComponent(Value: TComponent); override;
  published
    property Tag2: Integer read Width write Width;
  end;

  THandler = class
    Messages: string;
    procedure Skip(Reader: TReader; const Message: string; var Handled: Boolean);
  end;

procedure TChild.SetParentComponent(Value: TComponent);
begin
  NameAtParent := Name;
  if not (Value is TBox) then
    raise EInvalidOperation.Create('Control "' + Name + '" cannot be placed on a ' + Value.ClassName);
  ParentComp := Value;
end;

procedure THandler.Skip(Reader: TReader; const Message: string; var Handled: Boolean);
begin
  Messages := Messages + Message + ';';
  Handled := True;
end;

function Err(const Text: string): string;
var
  P: TPlain;
begin
  Result := '';
  P := TPlain.Create(nil);
  try
    ReadComponentFromText(Text, P, 'P.form');
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
  P.Free;
end;

var
  B: TBox;
  P: TPlain;
  C: TChild;
  H: THandler;
  S: TStringStream;
  R: TReader;
begin
  RegisterClasses([TBox, TPlain, TChild]);

  Section('accepted parent');
  B := TBox.Create(nil);
  ReadComponentFromText('object B: TBox'#10'  object Kid: TChild'#10'    Tag2 = 4'#10'  end'#10'end'#10, B, 'B.form');
  C := TChild(B.FindComponent('Kid'));
  Check(C <> nil, 'child created');
  CheckEqS('Kid', C.NameAtParent, 'name set before SetParentComponent');
  Check(C.ParentComp = B, 'parent set');
  CheckEqI(4, C.Width, 'properties read');
  B.Free;

  Section('refused parent is F0103');
  CheckEqS('EReadError: P.form(2,10): error F0103: Control "Kid" cannot be placed on a TPlain',
    Err('object P: TPlain'#10'  object Kid: TChild'#10'  end'#10'end'#10), 'message names the component');

  Section('OnError handles it');
  H := THandler.Create;
  P := TPlain.Create(nil);
  S := TStringStream.Create('object P: TPlain'#10'  object Kid: TChild'#10'    Tag2 = 9'#10'  end'#10 +
    '  object Two: TChild'#10'  end'#10'end'#10);
  R := TReader.Create(S, 4096);
  R.SourceName := 'P.form';
  R.OnError := H.Skip;
  R.ReadRootComponent(P);
  CheckEqS('P.form(2,10): error F0103: Control "Kid" cannot be placed on a TPlain;' +
    'P.form(5,10): error F0103: Control "Two" cannot be placed on a TPlain;', H.Messages, 'both reported');
  C := TChild(P.FindComponent('Kid'));
  Check(C <> nil, 'component kept');
  Check(C.ParentComp = nil, 'without a parent');
  CheckEqI(9, C.Width, 'its properties are read');
  Check(P.FindComponent('Two') <> nil, 'reading continues');
  R.Free;
  S.Free;
  P.Free;
  H.Free;
  Done;
end.
