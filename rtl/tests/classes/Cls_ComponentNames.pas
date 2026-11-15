// phase: 4
// covers: RTL-455, RTL-456, RTL-459
program Cls_ComponentNames;
{ TComponent names (classes.md section 8.1): SetName is a no-op for an equal name, validates identifiers and
  duplicates among the owner's components (ASCII case-insensitive; a case-only rename is allowed), raises
  EComponentError with the catalogue texts (RTL-455); published-field binding on SetName and on RemoveComponent
  (RTL-456); FindComponent: '' gives nil, case-insensitive, owned components only (RTL-459); GetNamePath = Name;
  InsertComponent refuses a duplicate name. }
uses
  SysUtils, Classes, TestKit;

type
  TPart = class(TComponent)
  end;

  THolder = class(TComponent)
  published
    Button1: TPart;
    Button2: TPart;
  end;

var
  Renames: string;

type
  TWatch = class(TComponent)
  protected
    procedure ValidateRename(AComponent: TComponent; const CurName, NewName: string); override;
  end;

procedure TWatch.ValidateRename(AComponent: TComponent; const CurName, NewName: string);
begin
  Renames := Renames + '[' + CurName + '>' + NewName + ']';
  inherited ValidateRename(AComponent, CurName, NewName);
end;

function NameError(C: TComponent; const NewName: string): string;
begin
  Result := '';
  try
    C.Name := NewName;
  except
    on E: EComponentError do
      Result := E.Message;
  end;
end;

var
  H: THolder;
  A, B, Inner: TPart;
  W: TWatch;
  Msg: string;
begin
  H := THolder.Create(nil);
  A := TPart.Create(H);
  B := TPart.Create(H);

  Section('SetName (RTL-455)');
  A.Name := 'Alpha';
  CheckEqS('Alpha', A.Name, 'Name');
  CheckEqS('Alpha', A.GetNamePath, 'GetNamePath is the Name');
  CheckEqS('"1abc" is not a valid component name', NameError(A, '1abc'), 'invalid identifier');
  CheckEqS('"a b" is not a valid component name', NameError(A, 'a b'), 'blank inside');
  CheckEqS('Alpha', A.Name, 'unchanged after an error');
  CheckEqS('A component named ALPHA already exists', NameError(B, 'ALPHA'), 'duplicate, case-insensitive');
  CheckEqS('', NameError(A, 'ALPHA'), 'case-only rename of itself');
  CheckEqS('ALPHA', A.Name, 'renamed');
  CheckEqS('', NameError(B, 'Beta'), 'valid name');
  CheckEqS('', NameError(B, ''), 'empty name');
  CheckEqS('', NameError(A, 'Beta'), 'the freed name is available');
  W := TWatch.Create(nil);
  A.Name := 'Alpha';
  Renames := '';
  W.InsertComponent(A);
  A.Name := 'Alpha';
  A.Name := 'Gamma';
  W.RemoveComponent(A);
  CheckEqS('[>Alpha][Alpha>Gamma][Gamma>]', Renames, 'ValidateRename on insert, rename (not for an equal name), remove');
  H.InsertComponent(A);
  W.Free;

  Section('field binding (RTL-456)');
  A.Name := 'Button1';
  Check(H.Button1 = A, 'SetName binds the published field');
  A.Name := 'Button2';
  Check(H.Button1 = nil, 'the old field is cleared');
  Check(H.Button2 = A, 'the new field is bound');
  B.Name := 'Button1';
  Check(H.Button1 = B, 'second component bound');
  H.RemoveComponent(B);
  Check(H.Button1 = nil, 'RemoveComponent clears the field');
  CheckEqS('Button1', B.Name, 'name kept');
  H.Button1 := A;
  B.Name := 'Other';
  H.InsertComponent(B);
  B.Name := 'Button1';
  Check(H.Button1 = B, 'binding overwrites');
  A.Name := '';
  Check(H.Button2 = nil, 'clearing the name clears the field');
  A.Name := 'Button2';
  H.Button2 := nil;
  B.Name := 'Bee';
  Check(H.Button1 = nil, 'a field holding the component is cleared');
  H.Button2 := B;
  A.Name := 'Ay';
  Check(H.Button2 = B, 'a field holding another component is left alone');
  H.Button2 := nil;

  Section('duplicates on insert');
  Inner := TPart.Create(nil);
  Inner.Name := 'Bee';
  Msg := '';
  try
    H.InsertComponent(Inner);
  except
    on E: EComponentError do
      Msg := E.Message;
  end;
  CheckEqS('A component named Bee already exists', Msg, 'InsertComponent checks the name');
  Check(Inner.Owner = nil, 'not inserted');

  Section('FindComponent (RTL-459)');
  Check(H.FindComponent('bee') = B, 'case-insensitive');
  Check(H.FindComponent('AY') = A, 'second');
  Check(H.FindComponent('') = nil, 'empty name');
  Check(H.FindComponent('Nope') = nil, 'absent');
  Inner.Name := 'Deep';
  B.InsertComponent(Inner);
  Check(H.FindComponent('Deep') = nil, 'not recursive');
  Check(B.FindComponent('Deep') = Inner, 'owned component');
  H.Free;
  Done;
end.
