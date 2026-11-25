// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ StrUtils - string helpers (docs: rtl/other-units.md section 3). Every function is ordinal and locale-independent;
  the "Text" forms compare ASCII case-insensitively (RTL-200). The Ansi* names are aliases of the plain functions
  with the same ordinal behaviour (RTL-620: deliberately not based on the OS collation). }
unit StrUtils;

interface

uses
  SysUtils, Types;

{ Copy semantics; Count <= 0 gives '' }
function LeftStr(const AText: string; const ACount: Integer): string;
function RightStr(const AText: string; const ACount: Integer): string;
function MidStr(const AText: string; const AStart, ACount: Integer): string;
{ an empty SubText is True for Starts / Ends and False for Contains (as Pos) }
function StartsStr(const ASubText, AText: string): Boolean;
function EndsStr(const ASubText, AText: string): Boolean;
function ContainsStr(const AText, ASubText: string): Boolean;
function StartsText(const ASubText, AText: string): Boolean;
function EndsText(const ASubText, AText: string): Boolean;
function ContainsText(const AText, ASubText: string): Boolean;
{ StringReplace with [rfReplaceAll] / [rfReplaceAll, rfIgnoreCase] }
function ReplaceStr(const AText, AFromText, AToText: string): string;
function ReplaceText(const AText, AFromText, AToText: string): string;
function PosEx(const SubStr, S: string; Offset: Integer = 1): Integer;
{ reverses the UTF-16 code units; a surrogate pair stays in order (high before low) }
function ReverseString(const AText: string): string;
function DupeString(const AText: string; ACount: Integer): string;
{ Copy(AText, 1, AStart - 1) + ASubText + Copy(AText, AStart + ALength, MaxInt) }
function StuffString(const AText: string; AStart, ALength: Integer; const ASubText: string): string;
function IfThen(AValue: Boolean; const ATrue: string; const AFalse: string = ''): string;
{ the first index, or -1 }
function IndexStr(const AText: string; const AValues: array of string): Integer;
function IndexText(const AText: string; const AValues: array of string): Integer;
function MatchStr(const AText: string; const AValues: array of string): Boolean;
function MatchText(const AText: string; const AValues: array of string): Boolean;
{ splits at each character of Delimiters; adjacent delimiters give empty items; '' gives an empty array }
function SplitString(const S, Delimiters: string): TStringDynArray;

function AnsiLeftStr(const AText: string; const ACount: Integer): string;
function AnsiRightStr(const AText: string; const ACount: Integer): string;
function AnsiMidStr(const AText: string; const AStart, ACount: Integer): string;
function AnsiStartsStr(const ASubText, AText: string): Boolean;
function AnsiEndsStr(const ASubText, AText: string): Boolean;
function AnsiContainsStr(const AText, ASubText: string): Boolean;
function AnsiStartsText(const ASubText, AText: string): Boolean;
function AnsiEndsText(const ASubText, AText: string): Boolean;
function AnsiContainsText(const AText, ASubText: string): Boolean;
function AnsiReplaceStr(const AText, AFromText, AToText: string): string;
function AnsiReplaceText(const AText, AFromText, AToText: string): string;
function AnsiIndexStr(const AText: string; const AValues: array of string): Integer;
function AnsiIndexText(const AText: string; const AValues: array of string): Integer;
function AnsiMatchStr(const AText: string; const AValues: array of string): Boolean;
function AnsiMatchText(const AText: string; const AValues: array of string): Boolean;
function AnsiReverseString(const AText: string): string;

implementation

function LeftStr(const AText: string; const ACount: Integer): string;
begin
  if ACount <= 0 then
    Result := ''
  else
    Result := Copy(AText, 1, ACount);
end;

function RightStr(const AText: string; const ACount: Integer): string;
begin
  if ACount <= 0 then
    Result := ''
  else if ACount >= Length(AText) then
    Result := AText
  else
    Result := Copy(AText, Length(AText) - ACount + 1, ACount);
end;

function MidStr(const AText: string; const AStart, ACount: Integer): string;
begin
  if ACount <= 0 then
    Result := ''
  else
    Result := Copy(AText, AStart, ACount);
end;

function StartsStr(const ASubText, AText: string): Boolean;
begin
  Result := (Length(ASubText) <= Length(AText)) and (Copy(AText, 1, Length(ASubText)) = ASubText);
end;

function EndsStr(const ASubText, AText: string): Boolean;
begin
  Result := (Length(ASubText) <= Length(AText)) and
    (Copy(AText, Length(AText) - Length(ASubText) + 1, Length(ASubText)) = ASubText);
end;

function ContainsStr(const AText, ASubText: string): Boolean;
begin
  Result := Pos(ASubText, AText) > 0;
end;

function StartsText(const ASubText, AText: string): Boolean;
begin
  Result := (Length(ASubText) <= Length(AText)) and SameText(Copy(AText, 1, Length(ASubText)), ASubText);
end;

function EndsText(const ASubText, AText: string): Boolean;
begin
  Result := (Length(ASubText) <= Length(AText)) and
    SameText(Copy(AText, Length(AText) - Length(ASubText) + 1, Length(ASubText)), ASubText);
end;

function ContainsText(const AText, ASubText: string): Boolean;
begin
  Result := Pos(UpperCase(ASubText), UpperCase(AText)) > 0;
end;

function ReplaceStr(const AText, AFromText, AToText: string): string;
begin
  Result := StringReplace(AText, AFromText, AToText, [rfReplaceAll]);
end;

function ReplaceText(const AText, AFromText, AToText: string): string;
begin
  Result := StringReplace(AText, AFromText, AToText, [rfReplaceAll, rfIgnoreCase]);
end;

function PosEx(const SubStr, S: string; Offset: Integer): Integer;
begin
  Result := Pos(SubStr, S, Offset);
end;

function IsHigh(C: Char): Boolean;
begin
  Result := (C >= #$D800) and (C <= #$DBFF);
end;

function IsLow(C: Char): Boolean;
begin
  Result := (C >= #$DC00) and (C <= #$DFFF);
end;

function ReverseString(const AText: string): string;
var
  I, N: Integer;
begin
  N := Length(AText);
  SetLength(Result, N);
  I := 1;
  while I <= N do
  begin
    if (I < N) and IsHigh(AText[I]) and IsLow(AText[I + 1]) then
    begin
      Result[N - I] := AText[I];
      Result[N - I + 1] := AText[I + 1];
      I := I + 2;
    end
    else
    begin
      Result[N - I + 1] := AText[I];
      I := I + 1;
    end;
  end;
end;

function DupeString(const AText: string; ACount: Integer): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to ACount do
    Result := Result + AText;
end;

function StuffString(const AText: string; AStart, ALength: Integer; const ASubText: string): string;
begin
  Result := Copy(AText, 1, AStart - 1) + ASubText + Copy(AText, AStart + ALength, MaxInt);
end;

function IfThen(AValue: Boolean; const ATrue: string; const AFalse: string): string;
begin
  if AValue then
    Result := ATrue
  else
    Result := AFalse;
end;

function IndexStr(const AText: string; const AValues: array of string): Integer;
var
  I: Integer;
begin
  for I := 0 to High(AValues) do
    if AValues[I] = AText then
    begin
      Result := I;
      Exit;
    end;
  Result := -1;
end;

function IndexText(const AText: string; const AValues: array of string): Integer;
var
  I: Integer;
begin
  for I := 0 to High(AValues) do
    if SameText(AValues[I], AText) then
    begin
      Result := I;
      Exit;
    end;
  Result := -1;
end;

function MatchStr(const AText: string; const AValues: array of string): Boolean;
begin
  Result := IndexStr(AText, AValues) >= 0;
end;

function MatchText(const AText: string; const AValues: array of string): Boolean;
begin
  Result := IndexText(AText, AValues) >= 0;
end;

function SplitString(const S, Delimiters: string): TStringDynArray;
var
  I, Start, N: Integer;
begin
  Result := nil;
  if S = '' then
    Exit;
  N := 1;
  for I := 1 to Length(S) do
    if Pos(S[I], Delimiters) > 0 then
      N := N + 1;
  SetLength(Result, N);
  N := 0;
  Start := 1;
  for I := 1 to Length(S) + 1 do
    if (I > Length(S)) or (Pos(S[I], Delimiters) > 0) then
    begin
      Result[N] := Copy(S, Start, I - Start);
      N := N + 1;
      Start := I + 1;
    end;
end;

function AnsiLeftStr(const AText: string; const ACount: Integer): string;
begin
  Result := LeftStr(AText, ACount);
end;

function AnsiRightStr(const AText: string; const ACount: Integer): string;
begin
  Result := RightStr(AText, ACount);
end;

function AnsiMidStr(const AText: string; const AStart, ACount: Integer): string;
begin
  Result := MidStr(AText, AStart, ACount);
end;

function AnsiStartsStr(const ASubText, AText: string): Boolean;
begin
  Result := StartsStr(ASubText, AText);
end;

function AnsiEndsStr(const ASubText, AText: string): Boolean;
begin
  Result := EndsStr(ASubText, AText);
end;

function AnsiContainsStr(const AText, ASubText: string): Boolean;
begin
  Result := ContainsStr(AText, ASubText);
end;

function AnsiStartsText(const ASubText, AText: string): Boolean;
begin
  Result := StartsText(ASubText, AText);
end;

function AnsiEndsText(const ASubText, AText: string): Boolean;
begin
  Result := EndsText(ASubText, AText);
end;

function AnsiContainsText(const AText, ASubText: string): Boolean;
begin
  Result := ContainsText(AText, ASubText);
end;

function AnsiReplaceStr(const AText, AFromText, AToText: string): string;
begin
  Result := ReplaceStr(AText, AFromText, AToText);
end;

function AnsiReplaceText(const AText, AFromText, AToText: string): string;
begin
  Result := ReplaceText(AText, AFromText, AToText);
end;

function AnsiIndexStr(const AText: string; const AValues: array of string): Integer;
begin
  Result := IndexStr(AText, AValues);
end;

function AnsiIndexText(const AText: string; const AValues: array of string): Integer;
begin
  Result := IndexText(AText, AValues);
end;

function AnsiMatchStr(const AText: string; const AValues: array of string): Boolean;
begin
  Result := MatchStr(AText, AValues);
end;

function AnsiMatchText(const AText: string; const AValues: array of string): Boolean;
begin
  Result := MatchText(AText, AValues);
end;

function AnsiReverseString(const AText: string): string;
begin
  Result := ReverseString(AText);
end;

end.
