// covers: LANG-661
program ErrIntfMissingMethod;

{ B has the wrong result type and C is missing: one E2651 per unimplemented method, at the listed interface. }

type
  IFoo = interface
    procedure A;
    function B(X: Integer): Integer;
    procedure C;
  end;

  TFoo = class(TInterfacedObject, IFoo)
    procedure A;
    function B(X: Integer): string;
  end;

procedure TFoo.A;
begin
end;

function TFoo.B(X: Integer): string;
begin
  Result := '';
end;

begin
end.
