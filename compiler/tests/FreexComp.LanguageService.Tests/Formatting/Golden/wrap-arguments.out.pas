program WrapArguments;

type
  TDigits = set of 0..9;

var
  Digits: TDigits;
  FirstValue, SecondValue, ThirdValue, FourthValue: Integer;
begin
  Writeln('The first value is ', FirstValue, ', the second value is ', SecondValue, ', the third value is ', ThirdValue,
    ', the fourth value is ', FourthValue);
  Digits := [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5,
    6, 7, 8, 9];
  if FirstValue > 0 then
    Writeln(FirstValue, SecondValue, ThirdValue, FourthValue, FirstValue, SecondValue, ThirdValue, FourthValue,
      FirstValue);
end.
