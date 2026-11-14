// covers: LANG-124, LANG-125, LANG-126, LANG-920
program IntegerLiterals;
begin
  Writeln(255, ' ', $FF, ' ', $ff, ' ', %11111111, ' ', &377);
  Writeln(0, ' ', $0, ' ', %0, ' ', &0);
  Writeln(&7, ' ', &10, ' ', &777);
  Writeln(1_000_000, ' ', $FF_FF, ' ', %1010_1010, ' ', 1_2_3);
  Writeln($7FFFFFFF, ' ', 2147483647);
  Writeln($80000000, ' ', $FFFFFFFF);
  Writeln(2147483648, ' ', 4294967296);
  Writeln(9223372036854775807, ' ', $7FFFFFFFFFFFFFFF);
  Writeln(9223372036854775808, ' ', $FFFFFFFFFFFFFFFF);
  Writeln(18446744073709551615);
  Writeln(-2147483648, ' ', -9223372036854775808);
  Writeln($DEAD_BEEF, ' ', %1111_0000_1111_0000);
end.
