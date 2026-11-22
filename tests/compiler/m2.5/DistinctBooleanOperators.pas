// covers: LANG-205, LANG-215
program DistinctBooleanOperators;

{ LANG-205, operators on a distinct Boolean type: unary `not` keeps the operand's type; a binary `and`/`or`/`xor`
  whose operands are the same type yields that type; operands of two different members of the Boolean family
  (another distinct type, Boolean itself, a literal, ByteBool) yield Boolean. The same holds for constant
  expressions. The result types are observed through overload resolution: each R overload prints the name of its
  parameter type. }

type
  TFlag = type Boolean;
  TOther = type Boolean;

const
  CF = TFlag(True);
  CO = TOther(False);

procedure R(const B: Boolean); overload;
begin
  Writeln('Boolean ', B);
end;

procedure R(const B: TFlag); overload;
begin
  Writeln('TFlag ', B);
end;

procedure R(const B: TOther); overload;
begin
  Writeln('TOther ', B);
end;

procedure R(const B: ByteBool); overload;
begin
  Writeln('ByteBool ', B);
end;

var
  F, G: TFlag;
  O: TOther;
  B: Boolean;
  BB: ByteBool;
begin
  F := True;
  G := False;
  O := True;
  B := False;
  BB := True;
  { unary not keeps the type }
  R(not F);
  R(not O);
  R(not B);
  R(not BB);
  R(not not F);
  { same-type binary operators keep the type }
  R(F and G);
  R(F or G);
  R(F xor G);
  R(O and O);
  R(not (F and G));
  R((F or G) and F);
  { two different members of the Boolean family yield Boolean }
  R(F and O);
  R(O or F);
  R(F xor B);
  R(B and F);
  R(F or True);
  R(False xor O);
  R(F and BB);
  R(BB or O);
  R((F and G) or O);
  R(not F and B);
  { the same rules for constant expressions }
  R(CF);
  R(not CF);
  R(CF and CF);
  R(CF xor CO);
  R(CF or False);
end.
