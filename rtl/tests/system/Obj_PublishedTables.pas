// phase: 4
// covers: ABI-144
program Obj_PublishedTables;
{ TObject.MethodAddress / MethodName / FieldAddress / ClassInfo / UnitName over the class RTTI tables (ABI 10.1, system.md
  section 13): names match ASCII case-insensitively, the class is searched before its ancestors, only entries with
  Visibility 3 (published) count whatever the RTTI scope (ABI-144), so public methods and private fields of the
  default broad scope are never found; MethodName maps a code address back to the first matching published name. }
{$POINTERMATH ON}
uses TestKit;

type
  TNotify = procedure(Sender: TObject) of object;
  TPlain = procedure of object;

  TBase = class
  private
    FHidden: Integer;
  public
    procedure Visible;
  published
    FFirst: TObject;
    FLink: TObject;
    procedure Click(Sender: TObject);
    procedure Shared; virtual;
  end;

  TChild = class(TBase)
  public
    FPub: Integer;
  published
    FSecond: TBase;
    procedure Shared; override;
    procedure Extra;
  end;

var
  Log: string;

procedure TBase.Visible; begin end;
procedure TBase.Click(Sender: TObject); begin Log := Log + 'click ' + ClassName + ';'; end;
procedure TBase.Shared; begin Log := Log + 'base shared;'; end;
procedure TChild.Shared; begin Log := Log + 'child shared;'; end;
procedure TChild.Extra; begin end;

var
  B: TBase;
  C: TChild;
  M: TMethod;
  P: Pointer;
begin
  B := TBase.Create;
  C := TChild.Create;
  B.FHidden := 1;

  Section('MethodAddress');
  Check(TBase.MethodAddress('Click') <> nil, 'base Click');
  Check(TBase.MethodAddress('cLICK') = TBase.MethodAddress('Click'), 'case-insensitive');
  Check(TChild.MethodAddress('click') = TBase.MethodAddress('Click'), 'inherited from ancestor');
  Check(TChild.MethodAddress('Shared') <> TBase.MethodAddress('Shared'), 'override found in class first');
  Check(TBase.MethodAddress('Extra') = nil, 'descendant method not in ancestor');
  Check(TBase.MethodAddress('Visible') = nil, 'public method not found');
  Check(TBase.MethodAddress('') = nil, 'empty name');
  Check(TBase.MethodAddress('Clic') = nil, 'prefix is not a match');
  Check(TBase.MethodAddress('Clicks') = nil, 'longer name is not a match');
  Check(C.MethodAddress('Extra') <> nil, 'through an instance');
  M.Code := C.MethodAddress('Click');
  M.Data := Pointer(C);
  TNotify(M)(nil);
  M.Code := C.MethodAddress('Shared');
  TPlain(M)();
  M.Code := B.MethodAddress('Shared');
  M.Data := Pointer(B);
  TPlain(M)();
  CheckEqS('click TChild;child shared;base shared;', Log, 'calls through TMethod');

  Section('MethodName');
  CheckEqS('Click', TChild.MethodName(TBase.MethodAddress('click')), 'declared casing, ancestor');
  CheckEqS('Shared', TChild.MethodName(TChild.MethodAddress('shared')), 'override');
  CheckEqS('Shared', TBase.MethodName(TBase.MethodAddress('shared')), 'base');
  CheckEqS('', TBase.MethodName(TChild.MethodAddress('Extra')), 'not in ancestor');
  CheckEqS('', TBase.MethodName(nil), 'nil address');

  Section('FieldAddress');
  P := C.FieldAddress('FFirst');
  Check(P = @C.FFirst, 'ancestor field');
  Check(C.FieldAddress('ffirst') = @C.FFirst, 'case-insensitive');
  Check(C.FieldAddress('FSecond') = @C.FSecond, 'own field');
  Check(C.FieldAddress('FLink') = @C.FLink, 'class-typed field');
  Check(B.FieldAddress('FSecond') = nil, 'descendant field not in ancestor');
  Check(C.FieldAddress('FHidden') = nil, 'private field not found');
  Check(C.FieldAddress('FPub') = nil, 'public field not found');
  Check(C.FieldAddress('Nothing') = nil, 'missing');

  Section('ClassInfo');
  Check(TChild.ClassInfo = TypeInfo(TChild), 'class');
  Check(C.ClassInfo = TypeInfo(TChild), 'instance');
  Check(TObject.ClassInfo = TypeInfo(TObject), 'TObject');
  CheckEqS('TBase', PString(PByte(TBase.ClassInfo) + 8)^, 'name in header');
  CheckEqS('Obj_PublishedTables', TChild.UnitName, 'UnitName');
  CheckEqS('System', TObject.UnitName, 'UnitName of TObject');

  C.Free;
  B.Free;
  Done;
end.
