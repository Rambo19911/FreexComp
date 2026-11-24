// phase: 8
// covers: RTL-977
// opc-run: tempdir
program LongPaths;
{ platform-layer.md "Long paths": the RTL file routines with a full path of 300 or more characters inside the fresh
  folder ParamStr(1). The program reads the system long-path policy itself: on - every call works without truncation;
  off - every call is refused with the usual False result, error code or exception. Both print the same text. }
uses LongPathKit, TestKit;

begin
  RunLongPaths(ParamStr(1), LongPathPolicyOn);
  Done;
end.
