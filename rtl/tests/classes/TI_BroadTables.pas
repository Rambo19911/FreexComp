// phase: 4
// covers: RTL-405
program TI_BroadTables;
{ RTL-405 in the default broad RTTI scope: GetFieldTable lists the private field with mvPrivate, GetMethodTable the
  public method, GetDeclaredPropTable the public property; GetPropList, GetPropInfo, MethodAddress and FieldAddress
  ignore the non-published entries. The body is the shared unit BroadTablesKit (also run by TI_BroadTablesPublished). }
uses
  BroadTablesKit;

begin
  Run;
end.
