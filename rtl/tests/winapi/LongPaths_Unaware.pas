// phase: 8
// covers: RTL-977
// opc: --manifest none
// opc-run: tempdir
program LongPaths_Unaware;
{ The checks of LongPaths in an image without a manifest: the operating system refuses long paths whatever the system
  policy says, so the refusal branch runs on every machine. }
uses LongPathKit, TestKit;

begin
  RunLongPaths(ParamStr(1), False);
  Done;
end.
