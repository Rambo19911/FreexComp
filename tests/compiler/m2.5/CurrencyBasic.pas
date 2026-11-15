// covers: C-218 (Currency: Int64 x 10000)
program CurrencyBasic;

type
  TMoney = type Currency;
  PInt64 = ^Int64;
  TLine = record
    Qty: Integer;
    Price: Currency;
  end;

const
  Price: Currency = 12.3456;
  Line: TLine = (Qty: 3; Price: 0.25);

var
  A, B, C: Currency;
  D: Double;
  I: Integer;
  M: TMoney;
  S: string;

function Twice(X: Currency): Currency;
begin
  Result := X + X;
end;

function Total(const L: TLine): Currency;
begin
  Result := L.Price * L.Qty;
end;

begin
  A := 1.5;
  B := 2;
  C := A + B;
  Writeln(C:0:4);
  C := A - B;
  Writeln(C:0:4);
  C := -C;
  Writeln(C:0:4);
  I := 3;
  C := A * I;
  Writeln(C:0:4);
  C := 10 / 3;
  Writeln(C:0:4);
  C := 2 / 3;
  Writeln(C:0:4);
  D := Price;
  Writeln(D:0:4);
  Writeln(Price:0:2);
  Writeln(SizeOf(Currency), ' ', SizeOf(TLine));
  Writeln(PInt64(@A)^);
  M := Twice(A);
  Writeln(M:0:1);
  Writeln(A < B, ' ', A = 1.5, ' ', B > I, ' ', C <> 0);
  A := 1.23456;
  Writeln(A:0:4);
  Writeln(Round(A), ' ', Trunc(B + A));
  Writeln(Abs(-A):0:4);
  A := 100000000000;
  B := 0.0001;
  C := A + B;
  Writeln(PInt64(@C)^);
  Writeln(Total(Line):0:2);
  Str(Price:0:3, S);
  Writeln(S);
  I := 7;
  C := I;
  C := C + 1;
  Writeln(C:0:0);
end.
