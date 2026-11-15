// covers: UIF-320, UIF-321, UIF-322, UIF-323
// needs: desktop
program Dump_Format;
{ U-050, layout.md UIF-320 ... 323: the canonical dump text (UTF-8 without BOM, LF, 2-space indentation, keys in
  the specified order, "controls": [] on one line for an empty list, negative integers with sign) of a fixture
  form, printed to stdout and compared byte for byte with the .out file (the fixture text shared with the
  designer's writer, DSG-LAY-050); controls in Controls[] order, recursively; unnamed controls and their subtrees
  and non-visual components are not listed; visible is the control's own property; JSON string escaping; the
  document of every form of Screen and of no form. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

type
  TBox = class(TCustomControl);
  TDot = class(TGraphicControl);
  TMainForm = class(TForm);

function NewControl(AClass: TControlClass; AParent: TWinControl; const AName: string; L, T, W, H: Integer): TControl;
begin
  Result := AClass.Create(AParent);
  Result.Name := AName;
  Result.Parent := AParent;
  Result.SetBounds(L, T, W, H);
end;

procedure Run;
var
  F, G: TForm;
  Panel, Unnamed: TControl;
  S: string;
begin
  Application.Initialize;
  S := LayoutDumpText(nil);
  Check(Pos('"forms": []', S) > 0, 'no form: empty forms list on one line');

  Section('escaping (UIF-320)');
  CheckEqS('""', LayoutDumpString(''), 'empty string');
  CheckEqS('"a\"b\\c"', LayoutDumpString('a"b\c'), 'quote and backslash');
  CheckEqS('"\u0001\u0009\u000A\u001F x"', LayoutDumpString(#1#9#10#31' x'), 'control characters as \u00XX');
  CheckEqS('"' + #228 + #8364 + '/"', LayoutDumpString(#228 + #8364 + '/'), 'non-ASCII and slash literally');

  Section('fixture form (UIF-321 ... 323)');
  F := TMainForm.CreateNew(nil);
  F.Name := 'MainForm';
  F.ClientWidth := 400;
  F.ClientHeight := 240;
  NewControl(TBox, F, 'NameEdit', 16, 16, 200, 23);
  Panel := NewControl(TBox, F, 'Panel', -5, 50, 300, 100);
  NewControl(TDot, TWinControl(Panel), 'Dot', 2, 3, 10, 10).Visible := False;
  Unnamed := NewControl(TBox, TWinControl(Panel), '', 0, 0, 50, 50);
  NewControl(TBox, TWinControl(Unnamed), 'InsideUnnamed', 1, 1, 5, 5);
  NewControl(TDot, F, 'Last', 0, 230, 400, 10);
  TComponent.Create(F).Name := 'NonVisual';
  S := LayoutDumpText(F);
  Check(Pos(#13, S) = 0, 'LF only');
  Check((Length(S) > 0) and (S[Length(S)] = #10), 'ends with LF');
  Section('dump of MainForm follows');
  Write(S);

  Section('every form of Screen in creation order (UIF-315)');
  G := TForm.CreateNew(nil);
  G.Name := 'Second';
  G.ClientWidth := 10;
  G.ClientHeight := 20;
  Write(LayoutDumpText(nil));
  G.Free;
  F.Free;
end;

begin
  Run;
  Done;
end.
