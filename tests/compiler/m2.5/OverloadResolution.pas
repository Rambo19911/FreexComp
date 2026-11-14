// covers: LANG-442, LANG-443
program OverloadResolution;

procedure P(X: Integer); overload;
begin
  Writeln('P Integer ', X);
end;

procedure P(X: Int64); overload;
begin
  Writeln('P Int64 ', X);
end;

procedure P(X: Double); overload;
begin
  Writeln('P Double');
end;

procedure P(const X: string); overload;
begin
  Writeln('P string ', X);
end;

procedure Q(X: Int64); overload;
begin
  Writeln('Q Int64 ', X);
end;

procedure Q(X: Double); overload;
begin
  Writeln('Q Double');
end;

procedure R(X: Byte); overload;
begin
  Writeln('R Byte ', X);
end;

procedure R(X: Double); overload;
begin
  Writeln('R Double');
end;

procedure C(X: Char); overload;
begin
  Writeln('C Char ', X);
end;

procedure C(const X: string); overload;
begin
  Writeln('C string ', X);
end;

procedure S(const X: string); overload;
begin
  Writeln('S string ', X);
end;

procedure S(const X: RawByteString); overload;
begin
  Writeln('S RawByteString ', X);
end;

procedure Two(A, B: Int64); overload;
begin
  Writeln('Two Int64 Int64');
end;

procedure Two(A: Integer; B: Double); overload;
begin
  Writeln('Two Integer Double');
end;

procedure V(var X: Integer); overload;
begin
  X := X + 1;
  Writeln('V Integer ', X);
end;

procedure V(var X: Int64); overload;
begin
  X := X + 2;
  Writeln('V Int64 ', X);
end;

var
  I: Integer;
  Big: Int64;
  Card: Cardinal;
  B: Byte;
  Sgl: Single;
  Ch: Char;
  Str: string;
  A: AnsiString;
begin
  P(1);
  P(5000000000);
  P(1.5);
  P('ab');
  I := 10;
  Big := 20;
  Card := 30;
  P(I);
  P(Big);
  P(Card);
  Sgl := 2.5;
  P(Sgl);
  Q(1);
  Q(I);
  Q(Sgl);
  R(1);
  R(300);
  B := 200;
  R(B);
  C('a');
  Ch := 'z';
  C(Ch);
  C('ab');
  Str := 'uni';
  C(Str);
  S(Str);
  S('lit');
  A := 'bytes';
  S(A);
  Two(I, I);
  V(I);
  V(Big);
end.
