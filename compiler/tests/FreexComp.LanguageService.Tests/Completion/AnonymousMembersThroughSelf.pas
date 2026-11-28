{ context: member-field }
program AnonymousMembersThroughSelf;
type
  TProc = reference to procedure;
  TAccount = class
  private
    FBalance: Integer;
    procedure Audit;
  public
    procedure Deposit(Amount: Integer);
  end;
procedure TAccount.Audit; begin end;
procedure TAccount.Deposit(Amount: Integer);
var
  Later: TProc;
begin
  Later := procedure
    begin
      {|}
    end;
  Later();
end;
begin
end.
{#has FBalance, Audit, Deposit, Amount, Later, Self, Free}
{#kind FBalance=Field, Audit=Method, Amount=Parameter, Self=Parameter}
{#top Amount}
