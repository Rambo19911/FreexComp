// covers: ABI-103, RTL-137
// opc-run: variants 1 2 3
program Sys_AssertDefault;
{ The default _Assert path (no AssertErrorProc): '<Msg> (<FileName>, line <Line>)' on ErrOutput, then runtime error
  227. Variant 1: a literal message. Variant 2: no message ('Assertion failed'). Variant 3: a message held in a
  string variable. A passing Assert and one under $C- do nothing. The variant is ParamStr(1). }
var
  V: string;
  Zero: Integer;
  S: string;
begin
  V := ParamStr(1);
  Zero := 0;
  S := 'from a ';
  S := S + 'variable';
  Assert(Zero = 0, 'not shown');
  {$C-}
  Assert(Zero = 1, 'disabled');
  {$C+}
  Writeln('start');
  case V[1] of
    '1':
      Assert(Zero = 1, 'literal message');
    '2':
      Assert(Zero > 0);
    '3':
      Assert(False, S);
  end;
  Writeln('not reached');
end.
