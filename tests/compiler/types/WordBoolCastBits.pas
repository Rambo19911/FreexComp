// covers: LANG-215, LANG-290
program WordBoolCastBits;

type
  TRec = record
    Pad: Byte;
    W: WordBool;
  end;

const
  CW = SmallInt(WordBool(True));
  CL = Cardinal(LongBool(True));

var
  B: ByteBool;
  W: WordBool;
  L: LongBool;
  R: TRec;

function GetW: WordBool;
begin
  Result := True;
end;

procedure Show;
begin
  WriteLn('ByteBool: ', ShortInt(B), ' ', SmallInt(B), ' ', Integer(B), ' ', Byte(B), ' ', Word(B), ' ', Cardinal(B));
  WriteLn('WordBool: ', ShortInt(W), ' ', SmallInt(W), ' ', Integer(W), ' ', Byte(W), ' ', Word(W), ' ', Cardinal(W));
  WriteLn('LongBool: ', ShortInt(L), ' ', SmallInt(L), ' ', Integer(L), ' ', Byte(L), ' ', Word(L), ' ', Cardinal(L));
  WriteLn('Boolean:  ', Ord(Boolean(B)), ' ', Ord(Boolean(W)), ' ', Ord(Boolean(L)), ' ', B, ' ', W, ' ', L);
end;

begin
  B := True;
  W := True;
  L := True;
  Show;                                 // all ones

  ShortInt(B) := 2;
  SmallInt(W) := 2;
  Integer(L) := 2;
  Show;                                 // the stored bits, still True

  B := False;
  W := False;
  L := False;
  Show;

  R.W := True;
  WriteLn('field: ', SmallInt(R.W), ' ', Word(R.W));
  SmallInt(R.W) := $0100;
  WriteLn('field: ', SmallInt(R.W), ' ', Byte(R.W), ' ', R.W);
  WriteLn('result: ', SmallInt(GetW), ' ', Integer(GetW), ' ', Word(GetW));
  WriteLn('const: ', CW, ' ', CL);
end.
