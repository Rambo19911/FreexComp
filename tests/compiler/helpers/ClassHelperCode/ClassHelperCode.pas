// covers: LANG-991
program ClassHelperCode;

{ code of a class helper: it uses the protected and strict protected members of a class of another unit, calls a protected
  virtual method unqualified, reaches the method it hides through 'inherited' (dispatched on the object's class), and its
  class methods without 'static' receive the class they are called on as Self }

uses
  UAccount;

type
  TAccountHelper = class helper for TAccount
    function Balance: Integer;
    function Rate: Integer;
    procedure Touch;
    function Describe: string;
    class function KindName: string;
    class function Make: TAccount;
  end;

function TAccountHelper.Balance: Integer;
begin
  Result := FBalance;
end;

function TAccountHelper.Rate: Integer;
begin
  Result := FRate;
end;

procedure TAccountHelper.Touch;
begin
  Changed;
end;

function TAccountHelper.Describe: string;
begin
  Result := 'helped ' + inherited Describe;
end;

class function TAccountHelper.KindName: string;
begin
  Result := Self.ClassName;
end;

class function TAccountHelper.Make: TAccount;
begin
  Result := Self.Create;
end;

var
  A, S, O: TAccount;

begin
  A := TAccount.Create;
  S := TSavings.Create;
  WriteLn(A.Balance, ' ', A.Rate);
  A.Touch;
  WriteLn(A.Balance);
  WriteLn(A.Describe);
  WriteLn(S.Describe);
  WriteLn(TAccount.KindName, ' ', TSavings.KindName, ' ', S.KindName);
  O := TSavings.Make;
  WriteLn(O.ClassName);
  O.Free;
  S.Free;
  A.Free;
end.
