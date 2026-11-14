// covers: LANG-250
program ErrSetBaseRange;
type
  TSigned = set of -1..5;
  TWide = set of 0..256;
  TInts = set of Integer;
  TWords = set of Word;
  TBigEnum = (A, B, C = 300);
  TEnums = set of TBigEnum;
begin
end.
