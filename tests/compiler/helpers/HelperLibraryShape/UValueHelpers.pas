unit UValueHelpers;

{ In the shape of the standard helper library (phase8/helpers-completion.md §10; rtl/phase8/standard-helpers.md §8 risk 2):
  a unit-level routine named like a helper member, declared before the helpers; constants (literals, High / Low, the
  smallest positive Double); an overload set of instance and static methods (one inline); static members with an out
  parameter; a default parameter whose default is an element of a scoped enumeration named False / True; a class function
  named Create; an array property (zero-based); a property named like an intrinsic; a method returning TArray<string>; method
  bodies in an include file. Inside a helper its members hide the outer names, so the bodies call those qualified:
  System.Length(Self), System.Copy(...), UValueHelpers.Trim(Self). }

interface

{$SCOPEDENUMS ON}
type
  TUseBoolStrs = (False, True);
{$SCOPEDENUMS OFF}

function Trim(const S: string): string;

type
  TIntegerHelper = record helper for Integer
  const
    MaxValue = 2147483647;
    MinValue = -2147483648;
  public
    function ToString: string; overload; inline;
    function ToString(Width: Integer): string; overload;
    class function Parse(const S: string): Integer; static;
    class function TryParse(const S: string; out Value: Integer): Boolean; static;
  end;

  TDoubleHelper = record helper for Double
  const
    Epsilon = 4.9406564584124654E-324;
    MaxValue = 1.7976931348623157E308;
  public
    class function ToString(Value: Double): string; overload; static; inline;
    function ToString: string; overload; inline;
    function IsPositive: Boolean;
  end;

  TStringHelper = record helper for string
  private
    function GetLength: Integer;
    function GetChars(Index: Integer): Char;
  public
    class function Create(C: Char; Count: Integer): string; static;
    function Trim: string; inline;
    function IsEmpty: Boolean;
    function Split(Separator: Char): TArray<string>;
    class function Join(const Separator: string; const Values: array of string): string; static;
    property Length: Integer read GetLength;
    property Chars[Index: Integer]: Char read GetChars;
  end;

  TBooleanHelper = record helper for Boolean
    function ToString(UseBoolStrs: TUseBoolStrs = TUseBoolStrs.False): string;
  end;

implementation

function Trim(const S: string): string;
var
  First, Last: Integer;
begin
  First := 1;
  Last := Length(S);
  while (First <= Last) and (S[First] <= ' ') do
    Inc(First);
  while (Last >= First) and (S[Last] <= ' ') do
    Dec(Last);
  Result := Copy(S, First, Last - First + 1);
end;

function TIntegerHelper.ToString: string;
begin
  Str(Self, Result);
end;

function TIntegerHelper.ToString(Width: Integer): string;
begin
  Str(Self:Width, Result);
end;

class function TIntegerHelper.Parse(const S: string): Integer;
begin
  if not TryParse(S, Result) then
    Result := MinValue;
end;

class function TIntegerHelper.TryParse(const S: string; out Value: Integer): Boolean;
var
  Code: Integer;
begin
  Val(S, Value, Code);
  Result := Code = 0;
end;

class function TDoubleHelper.ToString(Value: Double): string;
begin
  Str(Value:0:3, Result);
end;

function TDoubleHelper.ToString: string;
begin
  Result := ToString(Self);
end;

function TDoubleHelper.IsPositive: Boolean;
begin
  Result := Self >= Epsilon;
end;

{$I UValueHelpers.inc}

function TBooleanHelper.ToString(UseBoolStrs: TUseBoolStrs): string;
begin
  if UseBoolStrs = TUseBoolStrs.True then
    if Self then
      Result := 'True'
    else
      Result := 'False'
  else if Self then
    Result := '-1'
  else
    Result := '0';
end;

end.
