{ context: member-method }
program MemberSelf;
type
  TAccount = class
  private
    FBalance: Integer;
    procedure Audit;
  public
    procedure Deposit(Amount: Integer);
  end;
procedure TAccount.Audit; begin end;
procedure TAccount.Deposit(Amount: Integer);
begin
  Self.{|}
end;
begin
end.
{#has FBalance, Audit, Deposit, Free}
{#not Amount}
