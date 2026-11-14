// covers: LANG-386
program InlineVarLoop;

const
  Arr: array[1..3] of Integer = (10, 20, 30);

begin
  var Total := 0;
  for var I := 1 to 3 do
    Total := Total + I * Arr[I];
  Writeln(Total);
  for var X in Arr do
    Write(' ', X);
  Writeln;
  for var Ch in 'abc' do
    Write(Ch, '.');
  Writeln;
  for var I := 3 downto 1 do
    Write(I);
  Writeln;
  for var B: Byte := 1 to 2 do
    Write(SizeOf(B));
  Writeln;
end.