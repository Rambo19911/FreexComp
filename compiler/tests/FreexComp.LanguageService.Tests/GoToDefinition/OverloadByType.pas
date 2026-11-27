program OverloadByType;
procedure Show(I: Integer); overload; forward;
procedure Show(const S: string); overload; forward;
procedure Show(I: Integer);
begin
  Writeln(I);
end;
procedure {#}Show(const S: string);
begin
  Writeln(S);
end;
begin
  {|}Show('text');
end.
