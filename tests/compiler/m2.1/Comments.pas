// covers: LANG-140, LANG-141, LANG-142
program Comments; { brace comment }
(* paren-star comment *)
// line comment
{ multi-line
  brace comment with (* inside *) and // inside
}
(* multi-line paren-star comment
   with { braces } inside *)
var
  { comment inside a declaration } X: Integer; // trailing comment
  Y(* between tokens *): Integer;

/// doc comment before a routine
procedure Show(N: Integer);
{** another doc comment form }
begin
  Writeln(N);
end;

begin
  X := 1; { X := 100; }
  // X := 200;
  (* X := 300; *)
  Y := X {comment} + (* comment *) 2;
  Show(X);
  Show(Y);
  { $DEFINE NOT_A_DIRECTIVE: a space after the brace makes it a comment }
{$IFDEF NOT_A_DIRECTIVE}
  Writeln('never');
{$ENDIF}
  // {$DEFINE INLINE_COMMENT} inside a line comment is ignored
{$IFDEF INLINE_COMMENT}
  Writeln('never');
{$ENDIF}
  (* a directive-looking text {$DEFINE INSIDE} inside a comment is ignored *)
  { the first closing brace ends a brace comment: { is plain text here }
{$IFDEF INSIDE}
  Writeln('never');
{$ENDIF}
  Writeln('{not a comment}', ' (*nor this*) ', '//nor this');
  (*$DEFINE PARENSTAR*)
{$IFDEF PARENSTAR}
  Writeln('paren-star directive');
{$ENDIF}
  Writeln('end'); //
end.
