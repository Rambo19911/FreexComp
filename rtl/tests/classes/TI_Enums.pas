// phase: 4
// covers: RTL-400
program TI_Enums;
{ TypInfo layout records and the enumeration and set name functions (classes.md section 1, ABI section 10):
  GetTypeName/GetTypeKind/GetTypeData over the blocks the compiler emits for TypeInfo(T); enumeration names in
  declared casing, out of range ''; GetEnumValue case-insensitive, -1 if unknown; subranges use the names of their
  base enumeration; Boolean is an enumeration; SetToString / StringToSet with and without brackets, unknown
  element raises EPropertyConvertError; class data (ClassType, ParentInfo, UnitName). }
uses
  SysUtils, TypInfo, TestKit;

type
  TColor = (clRed, clGreen, clBlue, clYellow);
  TWarm = clGreen..clYellow;
  TColors = set of TColor;
  TSmall = -5..100;
  TBase = class
  end;
  TDerived = class(TBase)
  end;

var
  TI: PTypeInfo;
  D: PTypeData;
  Raised: Boolean;
begin
  Section('header and data');
  TI := TypeInfo(TColor);
  CheckEqS('TColor', GetTypeName(TI), 'enum name');
  Check(GetTypeKind(TI) = tkEnumeration, 'enum kind');
  Check(TI^.Kind = tkEnumeration, 'Kind field');
  D := GetTypeData(TI);
  CheckEqI(0, D^.MinValue, 'MinValue');
  CheckEqI(3, D^.MaxValue, 'MaxValue');
  Check(D^.OrdType = otUByte, 'OrdType of a small enum');
  Check(D^.BaseType = TI, 'a base enum is its own base');
  CheckEqI(4, GetEnumNameCount(TI), 'name count');
  TI := TypeInfo(TSmall);
  Check(TI^.Kind = tkInteger, 'subrange kind');
  CheckEqI(-5, GetTypeData(TI)^.MinValue, 'subrange min');
  CheckEqI(100, GetTypeData(TI)^.MaxValue, 'subrange max');
  Check(GetTypeData(TypeInfo(Integer))^.OrdType = otSLong, 'Integer is otSLong');
  Check(GetTypeData(TypeInfo(Word))^.OrdType = otUWord, 'Word is otUWord');
  Check(TypeInfo(Int64) <> nil, 'Int64 info');
  Check(PTypeInfo(TypeInfo(Int64))^.Kind = tkInt64, 'Int64 kind');
  Check(GetTypeData(TypeInfo(Double))^.FloatType = ftDouble, 'Double');
  Check(GetTypeData(TypeInfo(Single))^.FloatType = ftSingle, 'Single');
  Check(PTypeInfo(TypeInfo(string))^.Kind = tkUString, 'string kind');
  CheckEqI(1200, GetTypeData(TypeInfo(string))^.CodePage, 'string code page');

  Section('enum names');
  TI := TypeInfo(TColor);
  CheckEqS('clRed', GetEnumName(TI, 0), 'first');
  CheckEqS('clYellow', GetEnumName(TI, 3), 'last');
  CheckEqS('', GetEnumName(TI, 4), 'above range');
  CheckEqS('', GetEnumName(TI, -1), 'below range');
  CheckEqI(2, GetEnumValue(TI, 'clBlue'), 'value');
  CheckEqI(2, GetEnumValue(TI, 'CLBLUE'), 'value case-insensitive');
  CheckEqI(-1, GetEnumValue(TI, 'clPurple'), 'unknown');
  CheckEqI(-1, GetEnumValue(TI, ''), 'empty');
  CheckEqS('False', GetEnumName(TypeInfo(Boolean), 0), 'Boolean False');
  CheckEqS('True', GetEnumName(TypeInfo(Boolean), 1), 'Boolean True');
  CheckEqI(1, GetEnumValue(TypeInfo(Boolean), 'true'), 'Boolean value');

  Section('subrange of an enum');
  TI := TypeInfo(TWarm);
  Check(TI^.Kind = tkEnumeration, 'kind');
  CheckEqI(1, GetTypeData(TI)^.MinValue, 'min');
  Check(GetTypeData(TI)^.BaseType = TypeInfo(TColor), 'base type');
  CheckEqS('clBlue', GetEnumName(TI, 2), 'name from the base');
  CheckEqI(3, GetEnumValue(TI, 'clYellow'), 'value in range');
  CheckEqI(-1, GetEnumValue(TI, 'clRed'), 'value outside the subrange');

  Section('sets');
  TI := TypeInfo(TColors);
  Check(TI^.Kind = tkSet, 'set kind');
  Check(GetTypeData(TI)^.CompType = TypeInfo(TColor), 'CompType');
  CheckEqI(1, GetTypeData(TI)^.SetSize, 'set size');
  CheckEqS('clRed,clBlue', SetToString(TI, 5), 'names');
  CheckEqS('[clRed,clBlue]', SetToString(TI, 5, True), 'brackets');
  CheckEqS('', SetToString(TI, 0), 'empty');
  CheckEqS('[]', SetToString(TI, 0, True), 'empty brackets');
  CheckEqI(10, StringToSet(TI, 'clGreen,clYellow'), 'parse');
  CheckEqI(10, StringToSet(TI, '[ clgreen , CLYELLOW ]'), 'parse brackets and blanks');
  CheckEqI(0, StringToSet(TI, '[]'), 'parse empty');
  CheckEqI(0, StringToSet(TI, ''), 'parse empty string');
  Raised := False;
  try
    StringToSet(TI, 'clRed,clPink');
  except
    on E: EPropertyConvertError do
      Raised := E.Message = 'Invalid property value';
  end;
  CheckEqB(True, Raised, 'unknown element raises');

  Section('class data');
  TI := TypeInfo(TDerived);
  Check(TI^.Kind = tkClass, 'class kind');
  CheckEqS('TDerived', GetTypeName(TI), 'class name');
  D := GetTypeData(TI);
  Check(D^.ClassType = TDerived, 'ClassType');
  Check(D^.ParentInfo = TypeInfo(TBase), 'ParentInfo');
  CheckEqS('TI_Enums', string(D^.UnitName), 'UnitName');
  Check(GetTypeData(TypeInfo(TObject))^.ParentInfo = nil, 'TObject has no parent');
  Done;
end.
