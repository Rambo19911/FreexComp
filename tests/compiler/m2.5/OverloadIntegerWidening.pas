// covers: LANG-442
program OverloadIntegerWidening;

{ C-201: when every candidate widens the argument, the narrowest parameter type wins, at equal size the signed one
  (the IntToStr / IntToHex overload sets of SysUtils). }

function IntToStr(V: Integer): string; overload;
begin
  Result := 'Integer';
end;

function IntToStr(V: Int64): string; overload;
begin
  Result := 'Int64';
end;

function IntToStr(V: UInt64): string; overload;
begin
  Result := 'UInt64';
end;

function IntToHex(V: Int64; Digits: Integer): string; overload;
begin
  Result := 'Int64 hex';
end;

function IntToHex(V: UInt64; Digits: Integer): string; overload;
begin
  Result := 'UInt64 hex';
end;

var
  B: Byte;
  W: Word;
  C: Cardinal;
  I: Integer;
  Q: UInt64;
begin
  B := 1;
  W := 2;
  C := 3;
  I := 4;
  Q := 5;
  Writeln(IntToStr(B));
  Writeln(IntToStr(W));
  Writeln(IntToStr(C));
  Writeln(IntToStr(I));
  Writeln(IntToStr(Q));
  Writeln(IntToHex(C, 8));
  Writeln(IntToHex(W, 4));
  Writeln(IntToHex(Q, 16));
end.
