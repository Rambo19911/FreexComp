unit UAccount;

interface

type
  TAccount = class
  strict private
    FSecret: Integer;
  private
    FId: Integer;
  strict protected
    FRate: Integer;
  protected
    FBalance: Integer;
    procedure Changed; virtual;
  public
    constructor Create;
    function Describe: string; virtual;
  end;

  TSavings = class(TAccount)
  public
    function Describe: string; override;
  end;

implementation

constructor TAccount.Create;
begin
  FBalance := 100;
  FRate := 3;
  FSecret := 0;
  FId := FSecret;
end;

procedure TAccount.Changed;
begin
  Inc(FBalance);
end;

function TAccount.Describe: string;
begin
  Result := 'account';
end;

function TSavings.Describe: string;
begin
  Result := 'savings';
end;

end.
