// covers: UIF-500
program Gfx_Colors;
{ U-003, graphics.md section 1: TColor encoding, the complete constant table (values are normative),
  ColorToRGB (system colours through GetSysColor, special values masked), ColorToString (name or $ + 8 hex
  digits; aliases never returned), StringToColor (case-insensitive name or StrToInt text, else EConvertError),
  ColorToIdent / IdentToColor / GetColorValues, and the RegisterIntegerConsts registration for TColor. }
uses
  Windows, SysUtils, Classes, Graphics, TestKit;

type
  TEntry = record
    Name: string;
    Value: Integer;
  end;

  TCollector = class
    Names: TStringList;
    procedure Add(const S: string);
  end;

const
  { graphics.md section 1.1, copied from the spec tables: explicit RGB, special, then system colours }
  Table: array[0..51] of TEntry = (
    (Name: 'clBlack'; Value: $000000), (Name: 'clMaroon'; Value: $000080), (Name: 'clGreen'; Value: $008000),
    (Name: 'clOlive'; Value: $008080), (Name: 'clNavy'; Value: $800000), (Name: 'clPurple'; Value: $800080),
    (Name: 'clTeal'; Value: $808000), (Name: 'clWhite'; Value: $FFFFFF), (Name: 'clGray'; Value: $808080),
    (Name: 'clSilver'; Value: $C0C0C0), (Name: 'clRed'; Value: $0000FF), (Name: 'clLime'; Value: $00FF00),
    (Name: 'clYellow'; Value: $00FFFF), (Name: 'clBlue'; Value: $FF0000), (Name: 'clFuchsia'; Value: $FF00FF),
    (Name: 'clAqua'; Value: $FFFF00), (Name: 'clMoneyGreen'; Value: $C0DCC0), (Name: 'clSkyBlue'; Value: $F0CAA6),
    (Name: 'clCream'; Value: $F0FBFF), (Name: 'clMedGray'; Value: $A4A0A0), (Name: 'clNone'; Value: $1FFFFFFF),
    (Name: 'clDefault'; Value: $20000000),
    (Name: 'clScrollBar'; Value: -16777216 + 0), (Name: 'clBackground'; Value: -16777216 + 1),
    (Name: 'clActiveCaption'; Value: -16777216 + 2), (Name: 'clInactiveCaption'; Value: -16777216 + 3),
    (Name: 'clMenu'; Value: -16777216 + 4), (Name: 'clWindow'; Value: -16777216 + 5),
    (Name: 'clWindowFrame'; Value: -16777216 + 6), (Name: 'clMenuText'; Value: -16777216 + 7),
    (Name: 'clWindowText'; Value: -16777216 + 8), (Name: 'clCaptionText'; Value: -16777216 + 9),
    (Name: 'clActiveBorder'; Value: -16777216 + 10), (Name: 'clInactiveBorder'; Value: -16777216 + 11),
    (Name: 'clAppWorkSpace'; Value: -16777216 + 12), (Name: 'clHighlight'; Value: -16777216 + 13),
    (Name: 'clHighlightText'; Value: -16777216 + 14), (Name: 'clBtnFace'; Value: -16777216 + 15),
    (Name: 'clBtnShadow'; Value: -16777216 + 16), (Name: 'clGrayText'; Value: -16777216 + 17),
    (Name: 'clBtnText'; Value: -16777216 + 18), (Name: 'clInactiveCaptionText'; Value: -16777216 + 19),
    (Name: 'clBtnHighlight'; Value: -16777216 + 20), (Name: 'cl3DDkShadow'; Value: -16777216 + 21),
    (Name: 'cl3DLight'; Value: -16777216 + 22), (Name: 'clInfoText'; Value: -16777216 + 23),
    (Name: 'clInfoBk'; Value: -16777216 + 24), (Name: 'clHotLight'; Value: -16777216 + 26),
    (Name: 'clGradientActiveCaption'; Value: -16777216 + 27),
    (Name: 'clGradientInactiveCaption'; Value: -16777216 + 28),
    (Name: 'clMenuHighlight'; Value: -16777216 + 29), (Name: 'clMenuBar'; Value: -16777216 + 30));

procedure TCollector.Add(const S: string);
begin
  Names.Add(S);
end;

procedure CheckConstants;
begin
  Section('constants');
  CheckEqI(-16777216, clSystemColor, 'clSystemColor');
  CheckEqI($000000, clBlack, 'clBlack');
  CheckEqI($0000FF, clRed, 'clRed');
  CheckEqI($FF0000, clBlue, 'clBlue');
  CheckEqI($C0C0C0, clLtGray, 'clLtGray');
  CheckEqI($808080, clDkGray, 'clDkGray');
  CheckEqI($C0DCC0, clMoneyGreen, 'clMoneyGreen');
  CheckEqI($A4A0A0, clMedGray, 'clMedGray');
  CheckEqI($1FFFFFFF, clNone, 'clNone');
  CheckEqI($20000000, clDefault, 'clDefault');
  CheckEqI(-16777216 + 15, clBtnFace, 'clBtnFace');
  CheckEqI(-16777216 + 8, clWindowText, 'clWindowText');
  CheckEqI(-16777216 + 30, clMenuBar, 'clMenuBar');
  CheckEqI(-16777216 + 26, clHotLight, 'clHotLight');
  CheckEqI(SizeOf(Integer), SizeOf(TColor), 'SizeOf(TColor)');
end;

procedure CheckTable;
var
  I, V: Integer;
  S: string;
begin
  Section('table round trip');
  for I := 0 to High(Table) do
  begin
    CheckEqI(Table[I].Value, StringToColor(Table[I].Name), 'StringToColor ' + Table[I].Name);
    CheckEqS(Table[I].Name, ColorToString(Table[I].Value), 'ColorToString ' + Table[I].Name);
    V := 0;
    CheckEqB(True, IdentToColor(LowerCase(Table[I].Name), V), 'IdentToColor lower ' + Table[I].Name);
    CheckEqI(Table[I].Value, V, 'IdentToColor value ' + Table[I].Name);
    S := '';
    CheckEqB(True, ColorToIdent(Table[I].Value, S), 'ColorToIdent ' + Table[I].Name);
    CheckEqS(Table[I].Name, S, 'ColorToIdent name ' + Table[I].Name);
  end;
end;

procedure CheckAliasesAndText;
var
  V: Integer;
  S: string;
begin
  Section('aliases and numeric text');
  CheckEqI($C0C0C0, StringToColor('clLtGray'), 'clLtGray parses');
  CheckEqI($808080, StringToColor('CLDKGRAY'), 'clDkGray parses');
  CheckEqS('clSilver', ColorToString(clLtGray), 'clLtGray prints as clSilver');
  CheckEqS('clGray', ColorToString(clDkGray), 'clDkGray prints as clGray');
  CheckEqS('$00123456', ColorToString($123456), 'unnamed RGB');
  CheckEqS('$FF000019', ColorToString(TColor(clSystemColor or 25)), 'unnamed system colour');
  CheckEqS('$FFFFFFFF', ColorToString(-1), 'minus one');
  CheckEqI($FF, StringToColor('$FF'), 'hex text');
  CheckEqI(255, StringToColor('255'), 'decimal text');
  CheckEqI(-16777201, StringToColor('-16777201'), 'negative text');
  V := 7;
  CheckEqB(False, IdentToColor('clNoSuch', V), 'IdentToColor unknown');
  CheckEqI(7, V, 'IdentToColor unknown leaves value');
  CheckEqB(False, ColorToIdent($123456, S), 'ColorToIdent unknown');
  Expect('EConvertError', '''clNoSuch'' is not a valid integer value');
  try
    StringToColor('clNoSuch');
    Check(False, 'StringToColor unknown raises');
  except
    on E: Exception do
      CheckRaises('StringToColor unknown raises', E.ClassName, E.Message);
  end;
end;

procedure CheckToRGB;
begin
  Section('ColorToRGB');
  CheckEqI($0000FF, ColorToRGB(clRed), 'explicit RGB unchanged');
  CheckEqI($123456, ColorToRGB($7F123456), 'high byte masked');
  CheckEqI(GetSysColor(COLOR_BTNFACE), ColorToRGB(clBtnFace), 'clBtnFace via GetSysColor');
  CheckEqI(GetSysColor(COLOR_WINDOWTEXT), ColorToRGB(clWindowText), 'clWindowText via GetSysColor');
  CheckEqI($FFFFFF, ColorToRGB(clNone), 'clNone masked');
  CheckEqI($000000, ColorToRGB(clDefault), 'clDefault masked');
  CheckEqI($332211, RGB($11, $22, $33), 'RGB from Windows');
end;

procedure CheckValuesAndRegistration;
var
  C: TCollector;
  ToInt: TIdentToInt;
  ToIdent: TIntToIdent;
  V: Integer;
  S: string;
begin
  Section('GetColorValues');
  C := TCollector.Create;
  C.Names := TStringList.Create;
  try
    GetColorValues(C.Add);
    CheckEqI(Length(Table), C.Names.Count, 'one name per table entry');
    CheckEqS('clBlack', C.Names[0], 'first name');
    CheckEqS('clMenuBar', C.Names[C.Names.Count - 1], 'last name');
    CheckEqI(-1, C.Names.IndexOf('clLtGray'), 'alias not listed');
  finally
    C.Names.Free;
    C.Free;
  end;

  Section('registration');
  ToInt := FindIdentToInt(TypeInfo(TColor));
  ToIdent := FindIntToIdent(TypeInfo(TColor));
  Check(Assigned(ToInt), 'IdentToInt registered for TColor');
  Check(Assigned(ToIdent), 'IntToIdent registered for TColor');
  V := 0;
  CheckEqB(True, ToInt('clBtnFace', V), 'registered IdentToInt works');
  CheckEqI(clBtnFace, V, 'registered IdentToInt value');
  CheckEqB(True, ToIdent(clWhite, S), 'registered IntToIdent works');
  CheckEqS('clWhite', S, 'registered IntToIdent name');
end;

begin
  CheckConstants;
  CheckTable;
  CheckAliasesAndText;
  CheckToRGB;
  CheckValuesAndRegistration;
  Done;
end.
