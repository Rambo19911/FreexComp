// covers: LANG-210, LANG-213, LANG-303
program EnumBasics;
type
  TColor = (Red, Green, Blue);
const
  Favourite = Blue;
var
  C, D: TColor;

function Next(C: TColor): TColor;
begin
  if C = High(TColor) then
    Result := Low(TColor)
  else
    Result := Succ(C);
end;

procedure Show(var C: TColor);
var
  Local: TColor;
begin
  Local := C;
  C := Red;
  Writeln(Ord(Local), ' ', Ord(C));
end;

begin
  C := Green;
  Writeln(Ord(Red), ' ', Ord(C), ' ', Ord(Blue));
  C := Succ(C);
  Writeln(Ord(C));
  C := Pred(Pred(C));
  Writeln(Ord(C));
  D := Blue;
  Writeln(C < D, ' ', C = Red, ' ', D >= Green, ' ', C > D);
  Writeln(Ord(Low(TColor)), ' ', Ord(High(TColor)));
  Writeln(Ord(Low(C)), ' ', Ord(High(D)));
  Writeln(SizeOf(TColor), ' ', SizeOf(C));
  Inc(C);
  Writeln(Ord(C));
  Dec(C, 1);
  Writeln(Ord(C));
  C := TColor(2);
  Writeln(Ord(C));
  Writeln(Ord(TColor.Green), ' ', Ord(Favourite));
  if C = Blue then
    Writeln('blue');
  Writeln(Ord(Next(Blue)), ' ', Ord(Next(Red)));
  Show(D);
  Writeln(Ord(D));
end.
