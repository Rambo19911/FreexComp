{ TestKit - assertion helpers for the RTL test programs (testing.md §2, RTL-910...912).
  Every check prints "ok <Name>" or "FAIL <Name>: expected <e> got <a>"; Section prints "# <Title>";
  Done prints "# passed <p> failed <f>" and halts with exit code 1 when a check failed.
  Uses only System (RTL-912). Until overloads exist (M2.5) the typed comparisons carry a suffix:
  CheckEqS (string), CheckEqI (Int64), CheckEqB (Boolean). }
unit TestKit;

interface

procedure Check(Cond: Boolean; const Name: string);
procedure CheckEqS(const Expected, Actual: string; const Name: string);
procedure CheckEqI(Expected, Actual: Int64; const Name: string);
procedure CheckEqB(Expected, Actual: Boolean; const Name: string);
{ Passes when Expected and Actual are at most MaxUlps units in the last place apart (+0 and -0 are 0 apart);
  a NaN passes only against the identical bit pattern. }
procedure CheckEqF(Expected, Actual: Double; MaxUlps: Integer; const Name: string);
procedure Section(const Title: string);
procedure Done;

{ Helpers shared by tests that print values themselves. }
function QuoteStr(const S: string): string;
function IntStr(V: Int64): string;
function HexStr64(V: Int64): string;

implementation

var
  Passed, Failed: Integer;

function IntStr(V: Int64): string;
begin
  Str(V, Result);
end;

function HexStr64(V: Int64): string;
const
  Digits = '0123456789ABCDEF';
var
  I: Integer;
  U: UInt64;
begin
  U := UInt64(V);
  SetLength(Result, 16);
  for I := 16 downto 1 do
  begin
    Result[I] := Digits[Integer(U and 15) + 1];
    U := U shr 4;
  end;
end;

{ 'text' with '' for an embedded quote; a control character (< #32) is written as #nn outside the quotes. }
function QuoteStr(const S: string): string;
var
  I: Integer;
  Open: Boolean;
begin
  Result := '';
  Open := False;
  for I := 1 to Length(S) do
    if S[I] < #32 then
    begin
      if Open then
        Result := Result + '''';
      Open := False;
      Result := Result + '#' + IntStr(Ord(S[I]));
    end
    else
    begin
      if not Open then
        Result := Result + '''';
      Open := True;
      if S[I] = '''' then
        Result := Result + ''''''
      else
        Result := Result + S[I];
    end;
  if Open then
    Result := Result + ''''
  else if Result = '' then
    Result := '''''';
end;

function BoolStr(B: Boolean): string;
begin
  if B then
    Result := 'True'
  else
    Result := 'False';
end;

procedure Pass(const Name: string);
begin
  Passed := Passed + 1;
  Writeln('ok ', Name);
end;

procedure Fail(const Name, Expected, Actual: string);
begin
  Failed := Failed + 1;
  Writeln('FAIL ', Name, ': expected ', Expected, ' got ', Actual);
end;

procedure Check(Cond: Boolean; const Name: string);
begin
  if Cond then
    Pass(Name)
  else
  begin
    Failed := Failed + 1;
    Writeln('FAIL ', Name);
  end;
end;

procedure CheckEqS(const Expected, Actual: string; const Name: string);
begin
  if Expected = Actual then
    Pass(Name)
  else
    Fail(Name, QuoteStr(Expected), QuoteStr(Actual));
end;

procedure CheckEqI(Expected, Actual: Int64; const Name: string);
begin
  if Expected = Actual then
    Pass(Name)
  else
    Fail(Name, IntStr(Expected), IntStr(Actual));
end;

procedure CheckEqB(Expected, Actual: Boolean; const Name: string);
begin
  if Expected = Actual then
    Pass(Name)
  else
    Fail(Name, BoolStr(Expected), BoolStr(Actual));
end;

function DoubleBits(D: Double): Int64;
begin
  Move(D, Result, SizeOf(Result));
end;

function IsNaNBits(B: Int64): Boolean;
begin
  Result := ((B shr 52) and $7FF = $7FF) and (B and $FFFFFFFFFFFFF <> 0);
end;

{ Maps the bit pattern to an integer that orders like the value (-0 and +0 both map to 0). }
function Ordered(B: Int64): Int64;
begin
  if B < 0 then
    Result := -(B and $7FFFFFFFFFFFFFFF)
  else
    Result := B;
end;

function FloatText(D: Double): string;
var
  S: string;
begin
  Str(D, S);
  while (Length(S) > 0) and (S[1] = ' ') do
    Delete(S, 1, 1);
  Result := S + ' ($' + HexStr64(DoubleBits(D)) + ')';
end;

{$Q-}                                   { the distance is computed modulo 2^64 }
{$R-}
procedure CheckEqF(Expected, Actual: Double; MaxUlps: Integer; const Name: string);
var
  BE, BA, OE, OA: Int64;
  Distance: UInt64;
  Ok: Boolean;
begin
  BE := DoubleBits(Expected);
  BA := DoubleBits(Actual);
  if IsNaNBits(BE) or IsNaNBits(BA) then
    Ok := BE = BA
  else
  begin
    OE := Ordered(BE);
    OA := Ordered(BA);
    if OE >= OA then
      Distance := UInt64(OE) - UInt64(OA)
    else
      Distance := UInt64(OA) - UInt64(OE);
    Ok := (MaxUlps >= 0) and (Distance <= UInt64(MaxUlps));
  end;
  if Ok then
    Pass(Name)
  else
    Fail(Name, FloatText(Expected), FloatText(Actual));
end;

procedure Section(const Title: string);
begin
  Writeln('# ', Title);
end;

procedure Done;
begin
  Writeln('# passed ', Passed, ' failed ', Failed);
  if Failed > 0 then
    Halt(1);
end;

end.
