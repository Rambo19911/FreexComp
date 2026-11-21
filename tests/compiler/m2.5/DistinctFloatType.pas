// covers: LANG-205, LANG-281, LANG-442, CMP-405
program DistinctFloatType;

{ LANG-205: `type Double` / `type Single` / `type Currency` are new types: overloads on the source type and the
  distinct type resolve exactly (a converted argument such as a literal prefers Double), the RTTI name is the
  declared one (tkFloat with the source's FloatType), and values assign both ways. An alias stays the same type. }

{$POINTERMATH ON}

uses TypInfo;

type
  TMyDbl = type Double;
  TMySgl = type Single;
  TMyCur = type Currency;
  TAliasDbl = Double;

procedure Q(const D: Double); overload;
begin
  Writeln('Double ', D:0:2);
end;

procedure Q(const D: TMyDbl); overload;
begin
  Writeln('TMyDbl ', D:0:2);
end;

procedure Bump(var D: TMyDbl);
begin
  D := D + 1;
end;

procedure BumpPlain(var D: Double);
begin
  D := D + 10;
end;

function FloatKind(Info: Pointer): Integer;
begin
  Result := (PByte(Info) + 16)^;
end;

var
  M: TMyDbl;
  D: Double;
  A: TAliasDbl;
  S: TMySgl;
  C: TMyCur;
begin
  M := 1.5;
  D := 2.25;
  Q(M);
  Q(D);
  Q(2);
  Q(2.5);
  { assignment both ways, and with integers }
  D := M;
  M := D * 2 + 1;
  Writeln(D:0:2, ' ', M:0:2);
  Bump(M);
  Writeln(M:0:2);
  A := 0.5;
  BumpPlain(A);
  Q(A);
  S := M;
  C := 3;
  Writeln(S:0:1, ' ', C:0:2, ' ', SizeOf(TMySgl), ' ', SizeOf(TMyCur), ' ', M > D);
  Writeln(GetTypeName(TypeInfo(TMyDbl)), ' ', FloatKind(TypeInfo(TMyDbl)));
  Writeln(GetTypeName(TypeInfo(TMySgl)), ' ', FloatKind(TypeInfo(TMySgl)));
  Writeln(GetTypeName(TypeInfo(TMyCur)));
  Writeln(GetTypeName(TypeInfo(TAliasDbl)));
end.
