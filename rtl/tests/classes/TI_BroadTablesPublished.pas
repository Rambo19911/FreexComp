// phase: 4
// covers: RTL-405
// opc: --rtti published
program TI_BroadTablesPublished;
{ RTL-405 with --rtti published: the raw tables hold only published entries, and the published results are identical
  to TI_BroadTables (same shared unit BroadTablesKit). }
uses
  BroadTablesKit;

begin
  Run;
end.
