// covers: LANG-205, LANG-281, LANG-442, CMP-405
program DistinctBooleanType;

{ LANG-205: `type Boolean` is a new type with the representation of Boolean: overloads on Boolean and the distinct
  type resolve exactly (a TFlag value picks the TFlag overload, a Boolean value or literal the Boolean one), the
  RTTI name is the declared one, and values assign both ways. An alias stays the same type. }

uses TypInfo;

type
  TFlag = type Boolean;
  TAliasBool = Boolean;

procedure R(const B: Boolean); overload;
begin
  Writeln('Boolean ', B);
end;

procedure R(const B: TFlag); overload;
begin
  Writeln('TFlag ', B);
end;

procedure Toggle(var F: TFlag);
begin
  F := not F;
end;

procedure TogglePlain(var B: Boolean);
begin
  B := not B;
end;

function Both(const F: TFlag; const B: Boolean): TFlag;
begin
  Result := F and B;
end;

var
  F: TFlag;
  B: Boolean;
  A: TAliasBool;
begin
  F := True;
  B := False;
  R(F);
  R(B);
  R(True);
  R(Both(F, True));
  R(TFlag(B));
  R(Boolean(F));
  { assignment both ways }
  B := F;
  F := not B;
  Writeln(B, ' ', F, ' ', Ord(F), ' ', SizeOf(TFlag));
  Toggle(F);
  Writeln(F);
  { an alias is still Boolean: a var Boolean parameter accepts it }
  A := False;
  TogglePlain(A);
  R(A);
  Writeln(Both(F, B), ' ', F = B, ' ', F <> B);
  if F then
    Writeln('F is true')
  else
    Writeln('F is false');
  Writeln(GetTypeName(TypeInfo(TFlag)));
  Writeln(GetTypeName(TypeInfo(TAliasBool)));
  Writeln(GetTypeName(TypeInfo(Boolean)));
  Writeln(Ord(PTypeInfo(TypeInfo(TFlag))^.Kind) = Ord(tkEnumeration));
end.
