// phase: 4
// covers: RTL-470
program Cls_Registry;
{ Class registry and identifier maps (classes.md section 9): RegisterClass / RegisterClasses / RegisterClassAlias,
  GetClass (ASCII case-insensitive, nil when unknown), FindClass (EClassNotFound "Class <name> not found"),
  re-registering the same class is a no-op, another class under a registered name raises EFilerError
  "A class named <name> already exists", UnRegisterClass removes every name of the class; RegisterIntegerConsts with
  FindIdentToInt / FindIntToIdent (nil when none, the latest registration wins); IdentToInt / IntToIdent over a map. }
uses
  SysUtils, Classes, TestKit;

type
  TAlpha = class(TPersistent)
  end;
  TBeta = class(TPersistent)
  end;
  TGamma = class(TBeta)
  end;
  TMyColor = -$7FFFFFFF - 1..$7FFFFFFF;
  TMyCursor = -32768..32767;

const
  ColorMap: array[0..2] of TIdentMapEntry = (
    (Value: $0000FF; Name: 'clRed'),
    (Value: $00FF00; Name: 'clLime'),
    (Value: 0; Name: 'clBlack'));

function ColorToIdent(Int: Integer; var Ident: string): Boolean;
begin
  Result := IntToIdent(Int, Ident, ColorMap);
end;

function IdentToColor(const Ident: string; var Int: Integer): Boolean;
begin
  Result := IdentToInt(Ident, Int, ColorMap);
end;

function NoIdent(const Ident: string; var Int: Integer): Boolean;
begin
  Int := 42;
  Result := True;
end;

function NoInt(Int: Integer; var Ident: string): Boolean;
begin
  Ident := 'forty-two';
  Result := True;
end;

function Name(C: TPersistentClass): string;
begin
  if C = nil then
    Result := 'nil'
  else
    Result := C.ClassName;
end;

var
  Msg, S: string;
  I: Integer;
  ToInt: TIdentToInt;
  ToIdent: TIntToIdent;
begin
  Section('register and look up');
  Check(GetClass('TAlpha') = nil, 'unknown before registration');
  RegisterClass(TAlpha);
  CheckEqS('TAlpha', Name(GetClass('TAlpha')), 'GetClass');
  CheckEqS('TAlpha', Name(GetClass('talpha')), 'case-insensitive');
  RegisterClass(TAlpha);
  CheckEqS('TAlpha', Name(FindClass('TALPHA')), 'registering twice is a no-op');
  RegisterClasses([TBeta, TGamma]);
  CheckEqS('TBeta', Name(GetClass('TBeta')), 'RegisterClasses first');
  CheckEqS('TGamma', Name(GetClass('TGamma')), 'RegisterClasses second');
  RegisterClassAlias(TGamma, 'TOldGamma');
  CheckEqS('TGamma', Name(GetClass('toldgamma')), 'alias');

  Section('errors');
  Msg := '';
  try
    FindClass('TNope');
  except
    on E: EClassNotFound do
      Msg := E.Message;
  end;
  CheckEqS('Class TNope not found', Msg, 'FindClass of an unknown name');
  Msg := '';
  try
    RegisterClassAlias(TBeta, 'TAlpha');
  except
    on E: EFilerError do
      Msg := E.Message;
  end;
  CheckEqS('A class named TAlpha already exists', Msg, 'another class under a registered name');
  CheckEqS('TAlpha', Name(GetClass('TAlpha')), 'the first registration stays');

  Section('unregister');
  UnRegisterClass(TGamma);
  Check(GetClass('TGamma') = nil, 'class removed');
  Check(GetClass('TOldGamma') = nil, 'alias removed too');
  CheckEqS('TBeta', Name(GetClass('TBeta')), 'others stay');
  UnRegisterClasses([TAlpha, TBeta]);
  Check(GetClass('TAlpha') = nil, 'UnRegisterClasses first');
  Check(GetClass('TBeta') = nil, 'UnRegisterClasses second');
  RegisterClassAlias(TBeta, 'TAlpha');
  CheckEqS('TBeta', Name(GetClass('TAlpha')), 'the name is free again');

  Section('identifier maps');
  I := -1;
  CheckEqB(True, IdentToInt('CLLIME', I, ColorMap), 'IdentToInt found');
  CheckEqI($00FF00, I, 'IdentToInt value');
  I := 7;
  CheckEqB(False, IdentToInt('clBlue', I, ColorMap), 'IdentToInt unknown');
  CheckEqI(7, I, 'value untouched');
  S := '';
  CheckEqB(True, IntToIdent(0, S, ColorMap), 'IntToIdent found');
  CheckEqS('clBlack', S, 'IntToIdent name');
  CheckEqB(False, IntToIdent(5, S, ColorMap), 'IntToIdent unknown');

  Section('RegisterIntegerConsts');
  Check(not Assigned(FindIdentToInt(TypeInfo(TMyColor))), 'nothing registered');
  Check(not Assigned(FindIntToIdent(TypeInfo(TMyColor))), 'nothing registered (IntToIdent)');
  RegisterIntegerConsts(TypeInfo(TMyColor), IdentToColor, ColorToIdent);
  ToInt := FindIdentToInt(TypeInfo(TMyColor));
  Check(Assigned(ToInt), 'FindIdentToInt');
  I := 0;
  CheckEqB(True, ToInt('clRed', I), 'registered IdentToInt works');
  CheckEqI($FF, I, 'clRed');
  ToIdent := FindIntToIdent(TypeInfo(TMyColor));
  CheckEqB(True, ToIdent($00FF00, S), 'registered IntToIdent works');
  CheckEqS('clLime', S, 'clLime');
  Check(not Assigned(FindIdentToInt(TypeInfo(TMyCursor))), 'per type');
  RegisterIntegerConsts(TypeInfo(TMyColor), NoIdent, NoInt);
  ToInt := FindIdentToInt(TypeInfo(TMyColor));
  ToInt('x', I);
  CheckEqI(42, I, 'the latest registration wins');
  Done;
end.
