// covers: LANG-275
program ErrArrayConcat;

{ '+' joins dynamic arrays of identical element types only (E2011 otherwise); a static array or an open-array parameter
  beside a dynamic array is E2044: it has to be copied into a dynamic array first. }

type
  TInts = array of Integer;
  TStrs = array of string;

var
  A: TInts;
  S: TStrs;
  Fixed: array[0..2] of Integer;

procedure Join(const Open: array of Integer);
begin
  A := A + Open;
end;

begin
  A := A + S;
  A := Fixed + A;
end.
