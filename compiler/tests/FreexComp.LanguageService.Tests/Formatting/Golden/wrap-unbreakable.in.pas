program WrapUnbreakable;

var
  Message: string;
begin
  Message := 'This line holds one long string literal and no comma-separated list, so it cannot be broken and stays long';
  Writeln(Message + Message + Message + Message + Message + Message + Message + Message + Message + Message + Message + Message);
  Writeln('This first argument alone is already longer than the maximum line length of one hundred and twenty characters', 1, 2);
end.
