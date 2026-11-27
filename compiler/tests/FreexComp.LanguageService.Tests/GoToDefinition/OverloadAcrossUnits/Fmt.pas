unit Fmt;
interface
function Describe(I: Integer): string; overload;
function Describe(B: Boolean): string; overload;
implementation
function Describe(I: Integer): string;
begin
  Result := 'int';
end;
function {#}Describe(B: Boolean): string;
begin
  Result := 'bool';
end;
end.
