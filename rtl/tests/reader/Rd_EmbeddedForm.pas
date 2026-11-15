// phase: 4
// covers: RTL-482, RTL-483
program Rd_EmbeddedForm;
{ Embedded forms (classes.md section 10.2): the FORM directive files of the shared unit RdEmbedForms are registered by its
  initialization and found with FindResourceData (RTL-482); InitInheritedComponent reads the file of every class
  from the base-most below RootAncestor to the most derived one, with SourceName "<ClassName>.form", and returns
  whether any class had a file (RTL-483); InitComponentRes reads one named resource. }
uses
  SysUtils, Classes, TestKit, RdEmbedForms;

var
  Base: TEmbedBase;
  Derived: TEmbedDerived;
  Bad: TEmbedBad;
  None: TEmbedNone;
  Data: Pointer;
  Size: NativeInt;
  Msg: string;
begin
  Section('registration (RTL-482)');
  CheckEqB(True, FindResourceData('TEmbedBase', Data, Size), 'base form registered');
  CheckEqB(True, FindResourceData('tembedderived', Data, Size), 'derived form registered, case-insensitive');
  CheckEqB(False, FindResourceData('TEmbedNone', Data, Size), 'no form for TEmbedNone');

  Section('InitInheritedComponent (RTL-483)');
  Base := TEmbedBase.Create(nil);
  CheckEqB(True, InitInheritedComponent(Base, TComponent), 'base: found');
  CheckEqS('EmbedForm', Base.Name, 'root name');
  CheckEqS('base', Base.Caption, 'root property');
  Check(Base.A <> nil, 'child bound to its field');
  CheckEqS('part a', Base.A.Caption, 'child property');
  Base.Free;
  Derived := TEmbedDerived.Create(nil);
  CheckEqB(True, InitInheritedComponent(Derived, TComponent), 'derived: found');
  CheckEqS('derived', Derived.Caption, 'the derived file is read last');
  CheckEqI(10, Derived.Width, 'the base file is read first');
  Check((Derived.A <> nil) and (Derived.B <> nil), 'children of both files');
  CheckEqS('part a|part b', Derived.A.Caption + '|' + Derived.B.Caption, 'child properties of both files');
  CheckEqI(2, Derived.ComponentCount, 'two children');
  Derived.Free;
  Derived := TEmbedDerived.Create(nil);
  CheckEqB(True, InitInheritedComponent(Derived, TEmbedBase), 'RootAncestor excludes the base file');
  CheckEqS('derived', Derived.Caption, 'derived file read');
  CheckEqI(0, Derived.Width, 'base file not read');
  Check(Derived.A = nil, 'no base child');
  Derived.Free;
  None := TEmbedNone.Create(nil);
  CheckEqB(False, InitInheritedComponent(None, TComponent), 'no file at all');
  None.Free;
  Bad := TEmbedBad.Create(nil);
  Msg := '';
  try
    InitInheritedComponent(Bad, TComponent);
  except
    on E: EReadError do
      Msg := E.Message;
  end;
  CheckEqS('TEmbedBad.form(2,3): error F0102: Property Nope does not exist on TEmbedBad', Msg,
    'errors name the class file');
  Bad.Free;

  Section('InitComponentRes');
  Base := TEmbedBase.Create(nil);
  CheckEqB(True, InitComponentRes('TEmbedBase', Base), 'named resource read');
  CheckEqS('base', Base.Caption, 'its properties');
  CheckEqB(False, InitComponentRes('TNothing', Base), 'unknown resource');
  Base.Free;
  Done;
end.
