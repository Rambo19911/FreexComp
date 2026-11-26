// phase: 8
// covers: RTL-1105
// opc: --rtti published
program Cls_ReadFieldClassPublished;
{ RTL-1105 (task R-246) with --rtti published: the same checks and output as Cls_ReadFieldClass (shared unit
  ReadFieldClassKit). }
uses
  ReadFieldClassKit;

begin
  Run;
end.
