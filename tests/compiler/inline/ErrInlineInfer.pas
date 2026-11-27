// covers: LANG-574
program ErrInlineInfer;

{ var X := e cannot infer a type from nil, an empty set constructor, an anonymous method or a routine name that is not
  called (a procedure, a function that needs an argument): E2075 at the name, and its uses report nothing further.
  A later diagnostic on an inferred variable names its inferred type. }

procedure SomeProcedure;
begin
end;

function NeedsArgument(A: Integer): Integer;
begin
  Result := A;
end;

procedure Run;
begin
  var P := nil;
  var S := [];
  var F := procedure begin end;
  var G := SomeProcedure;
  var H := NeedsArgument;
  P := nil;
  G;
  var X := 5;
  X := 'text';
end;

begin
  Run;
end.
