// covers: RTL-136, RTL-214, RTL-215, ABI-103, ABI-124
// opc-run: variants 1 2 3 4
program SU_Assert;
{ SysUtils' AssertErrorProc handler (RTL-214) and unhandled-exception handler (RTL-215). Variant 1: a failed Assert
  raises EAssertionFailed with '<Msg> (<FileName>, line <Line>)' ('Assertion failed' without a message), which a
  handler catches; a passing Assert does nothing. Variant 2: an uncaught failed Assert is reported on ErrOutput as
  'Unhandled exception EAssertionFailed: <text>', exit code 217. Variant 3: an uncaught object that is not an Exception
  is reported by class name only. Variant 4: in a GUI program (IsConsole = False) the report goes to ShowErrorProc.
  The variant is ParamStr(1). }
uses
  SysUtils, TestKit;

type
  TThing = class
  end;

var
  Zero: Integer;

function AssertText(const Msg: string; WithMsg: Boolean): string;
begin
  Result := 'no exception';
  try
    if WithMsg then
      Assert(Zero = 1, Msg)
    else
      Assert(Zero = 1);
  except
    on E: EAssertionFailed do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

procedure Caught;
var
  S: string;
begin
  Section('caught assertions');
  CheckEqS('EAssertionFailed: boom (SU_Assert.pas, line 25)', AssertText('boom', True), 'message, file and line');
  CheckEqS('EAssertionFailed: Assertion failed (SU_Assert.pas, line 27)', AssertText('', False),
    'no message: Assertion failed');
  CheckEqS('EAssertionFailed: Assertion failed (SU_Assert.pas, line 25)', AssertText('', True),
    'empty message: Assertion failed');
  S := 'built';
  S := S + ' text';
  CheckEqS('EAssertionFailed: built text (SU_Assert.pas, line 25)', AssertText(S, True), 'message from a variable');
  S := 'not raised';
  try
    Assert(Zero = 0, 'fine');
  except
    S := 'raised';
  end;
  CheckEqS('not raised', S, 'a passing Assert does nothing');
  Check(@AssertErrorProc <> nil, 'AssertErrorProc installed');
  Check(@ExceptProc <> nil, 'ExceptProc installed');
  Done;
end;

procedure Show(const Text: string);
begin
  Writeln('show: ', Text);
end;

var
  V: string;
begin
  V := ParamStr(1);
  Zero := 0;
  case V[1] of
    '1':
      Caught;
    '2':
      begin
        Writeln('start');
        Assert(Zero = 1, 'uncaught');
        Writeln('not reached');
      end;
    '3':
      begin
        Writeln('start');
        raise TThing.Create;
        Writeln('not reached');
      end;
    '4':
      begin
        Writeln('start');
        IsConsole := False;
        ShowErrorProc := Show;
        raise EConvertError.Create('shown');
        Writeln('not reached');
      end;
  end;
end.
