// covers: LANG-575, LANG-576
program ForVarTypes;

{ A counted for var loop takes its type from both bounds: the start's natural type, widened when the end bound does not
  fit it. 0 to High(U) with U: UInt64 is a UInt64 loop (SizeOf 8); a loop that ends at High(UInt64) stops there; Byte
  bounds give a Byte loop; Char and enumeration bounds keep their type. A for var ... in loop has the element type of a
  dynamic array, Char for a string, the element type of a set and the type of an enumerator's Current. }

type
  TColor = (Red, Green, Blue);
  TDigits = set of 0..9;

  TCountdown = class
  private
    FLeft: Integer;
  public
    constructor Create(From: Integer);
    function GetEnumerator: TCountdown;
    function MoveNext: Boolean;
    function Current: Integer;
  end;

constructor TCountdown.Create(From: Integer);
begin
  FLeft := From + 1;
end;

function TCountdown.GetEnumerator: TCountdown;
begin
  Result := TCountdown.Create(FLeft - 1);
end;

function TCountdown.MoveNext: Boolean;
begin
  Dec(FLeft);
  Result := FLeft > 0;
end;

function TCountdown.Current: Integer;
begin
  Result := FLeft;
end;

const
  ColorNames: array[TColor] of string = ('Red', 'Green', 'Blue');

var
  U: UInt64;
  Top: UInt64;
  Lo, Hi: Byte;
  Count: NativeInt;
  Values: array of Integer;
  Text: string;
  Digits: TDigits;
  Countdown: TCountdown;

procedure Run;
begin
  for var I := 1 to 3 do
    Write(I, ' ');
  WriteLn('up');
  for var I := 3 downto 1 do
    Write(I, ' ');
  WriteLn('down');
  for var I := 1 to 3 do
    if I = 3 then
      WriteLn('Integer: SizeOf ', SizeOf(I));

  for var I := 0 to High(U) do
  begin
    if I = 0 then
      Write('UInt64: SizeOf ', SizeOf(I), ', values');
    Write(' ', I);
    if I = 2 then
      Break;
  end;
  WriteLn;

  Top := High(U);
  for var I := Top - 2 to Top do
    Write(I, ' ');
  WriteLn('near the top');

  Count := 3;
  for var I := 0 to Count - 1 do
    if I = 0 then
      WriteLn('NativeInt end: SizeOf ', SizeOf(I));

  Lo := 253;
  Hi := 255;
  for var B := Lo to Hi do
    Write(B, ' ');
  WriteLn('Byte');
  for var B := Lo to Hi do
    if B = Hi then
      WriteLn('Byte: SizeOf ', SizeOf(B));

  for var C := 'a' to 'e' do
    Write(C);
  WriteLn(' Char');
  for var C := 'a' to 'a' do
    WriteLn('Char: SizeOf ', SizeOf(C));

  for var E := Low(TColor) to High(TColor) do
    Write(ColorNames[E], ' ', Ord(E), ' ');
  WriteLn('TColor');

  Values := [10, 20, 30];
  for var X in Values do
    Write(X, ' ');
  WriteLn('array');

  Text := 'xyz';
  for var Ch in Text do
    Write(Ch, ' ', SizeOf(Ch), ' ');
  WriteLn('string');

  Digits := [1, 4, 9];
  for var D in Digits do
    Write(D, ' ');
  WriteLn('set');

  Countdown := TCountdown.Create(3);
  for var N in Countdown do
    Write(N, ' ');
  WriteLn('enumerator');
  Countdown.Free;
end;

begin
  Run;
end.
