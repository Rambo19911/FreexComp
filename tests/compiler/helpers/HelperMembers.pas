// covers: LANG-990
program HelperMembers;

{ the remaining members of helpers (LANG-990 (c), (d)): nested class and interface types that the helper's methods create and
  use; a property whose read names a protected field and whose write names a method of the helped class; a class property over
  a class var of the helper; an array property over a helper method; a record helper with a constant, a nested enumeration and
  a class var. One line per member. }

type
  TAccount = class
  protected
    FBalance: Integer;
    procedure SetBalance(Value: Integer);
  end;

  TAccountHelper = class helper for TAccount
  public
    type
      IRule = interface
        function Check(Amount: Integer): Boolean;
      end;
      TLimitRule = class(TInterfacedObject, IRule)
        Limit: Integer;
        constructor Create(ALimit: Integer);
        function Check(Amount: Integer): Boolean;
      end;
      TEntry = class
        Text: string;
        function Describe: string;
      end;
  public
    class var Opened: Integer;
    function NewEntry(const Text: string): TEntry;
    function Allowed(Amount: Integer): Boolean;
    function GetDigit(Index: Integer): Integer;
    property Balance: Integer read FBalance write SetBalance;
    class property OpenCount: Integer read Opened write Opened;
    property Digits[Index: Integer]: Integer read GetDigit;
  end;

  TIntHelper = record helper for Integer
  public
    const Base = 16;
    type TSign = (sNegative, sZero, sPositive);
    class var Count: Integer;
    function Sign: TSign;
  end;

procedure TAccount.SetBalance(Value: Integer);
begin
  Writeln('set balance ', Value);
  FBalance := Value;
end;

constructor TAccountHelper.TLimitRule.Create(ALimit: Integer);
begin
  inherited Create;
  Limit := ALimit;
end;

function TAccountHelper.TLimitRule.Check(Amount: Integer): Boolean;
begin
  Result := Amount <= Limit;
end;

function TAccountHelper.TEntry.Describe: string;
begin
  Result := 'entry ' + Text;
end;

function TAccountHelper.NewEntry(const Text: string): TEntry;
begin
  Result := TEntry.Create;
  Result.Text := Text;
end;

function TAccountHelper.Allowed(Amount: Integer): Boolean;
var
  Rule: IRule;
begin
  Rule := TLimitRule.Create(FBalance);
  Result := Rule.Check(Amount);
end;

function TAccountHelper.GetDigit(Index: Integer): Integer;
var
  I, N: Integer;
begin
  N := FBalance;
  for I := 1 to Index do
    N := N div 10;
  Result := N mod 10;
end;

function TIntHelper.Sign: TSign;
begin
  Inc(Count);
  if Self < 0 then
    Result := sNegative
  else if Self = 0 then
    Result := sZero
  else
    Result := sPositive;
end;

var
  A: TAccount;
  E: TAccountHelper.TEntry;
  N: Integer;
  S: TIntHelper.TSign;
begin
  A := TAccount.Create;
  A.Balance := 472;
  Writeln('balance ', A.Balance);
  E := A.NewEntry('rent');
  Writeln(E.Describe);
  E.Free;
  Writeln('allowed 400 ', A.Allowed(400), ', allowed 500 ', A.Allowed(500));
  TAccount.OpenCount := 3;
  Inc(TAccountHelper.Opened);
  Writeln('open count ', TAccount.OpenCount);
  Writeln('digits ', A.Digits[0], ' ', A.Digits[1], ' ', A.Digits[2]);
  N := -5;
  S := N.Sign;
  Writeln('sign ', Ord(S), ' ', Ord(0.Sign), ' ', Ord(High(TIntHelper.TSign)));
  Writeln('base ', Integer.Base, ' ', TIntHelper.Base);
  Writeln('count ', Integer.Count);
  A.Free;
end.
