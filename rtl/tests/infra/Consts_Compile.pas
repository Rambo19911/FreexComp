// covers: RTL-650
program Consts_Compile;
{ RTLConsts (other-units.md §6): the message catalogue compiles and its texts are those of sysutils.md §11 and
  classes.md §12. Prints three constants, one per catalogue, and a multi-line one. }
uses RTLConsts;

begin
  Writeln(SInvalidInteger);
  Writeln(SListIndexError);
  Writeln(SGenericItemNotFound);
  Writeln(SOSError);
  Writeln(SAccessViolation);
  Writeln(SFormDuplicateName);
end.
