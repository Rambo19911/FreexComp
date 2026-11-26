// phase: 8
// covers: RTL-1105
program Cls_ReadFieldClass;
{ RTL-1105 (task R-246) with the default broad RTTI scope: object and inline lines whose class is not registered are
  resolved from the published fields of the root's class chain (shared unit ReadFieldClassKit; the same output as
  Cls_ReadFieldClassPublished). }
uses
  ReadFieldClassKit;

begin
  Run;
end.
