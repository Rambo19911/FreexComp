{ context: expected-type }
program ExpectedSubrangeChar;
type
  TLower = 'a'..'z';
const
  First = 'a';
  Capital = 'A';
  Greeting = 'hello';
var
  Alpha: Integer;
  Key: Char;
  Letter: TLower;
begin
  Letter := {|}
end.
{#ranked Letter, Key, First}
{#unranked Capital, Greeting, Alpha, nil}
