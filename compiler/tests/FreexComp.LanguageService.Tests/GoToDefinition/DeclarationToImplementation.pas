program DeclarationToImplementation;
type
  TWorker = class
    procedure {|}Run;
  end;
procedure TWorker.{#}Run;
begin
  Writeln('run');
end;
begin
  TWorker.Create.Run;
end.
