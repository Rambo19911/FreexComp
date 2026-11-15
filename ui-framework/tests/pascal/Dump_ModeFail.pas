// covers: UIF-312
// opc-run: rawargs --dump-layout no-such-folder\sub\dump.json
program Dump_ModeFail;
{ U-050, layout.md UIF-312: when the dump cannot be written, Run reports "Cannot write layout dump: <reason>" on
  stderr and halts with exit code 2 (expected in Dump_ModeFail.exit and .err.txt); no form is shown. }
uses
  SysUtils, Classes, Controls, Forms;

var
  F: TForm;

begin
  Application.CreateForm(TForm, F);
  Application.Run;
  WriteLn('Run returned');
end.
