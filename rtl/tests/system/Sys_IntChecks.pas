// covers: RTL-044, RTL-130
// opc-run: variants 1 2 3
program Sys_IntChecks;
{$R+}
{$Q+}
{ Variant 1: div by zero -> 200. Variant 2: range check -> 201. Variant 3: overflow -> 215.
  The variant is the last character of the command line (ParamStr arrives with R-011). }
var
  P: PChar;
  Zero, Big, I: Integer;
  Small: Byte;
  A: array[1..3] of Integer;
begin
  P := CmdLine;
  while P^ <> #0 do
    Inc(P);
  Dec(P);
  Zero := 0;
  Big := MaxInt;
  I := 4;
  Writeln('start');
  case P^ of
    '1':
      Writeln(10 div Zero);
    '2':
      begin
        A[I] := 1;
        Small := I;
        Writeln(Small);
      end;
    '3':
      Writeln(Big + 1);
  end;
  Writeln('not reached');
end.
