// covers: LANG-771, LANG-773, LANG-776, LANG-777
program ErrNestedTypes;

type
  TOuter = class
  private
    FValue: Integer;
  public
    type
      TInner = class
        function Peek: Integer;
      end;

      TBad = class(TOuter)
      end;

      TNever = class;
  end;

function TOuter.TInner.Peek: Integer;
begin
  Result := FValue;
end;

procedure TOuter.TMissing.Run;
begin
end;

procedure Local;
type
  TLocal = class
  end;
begin
end;

begin
end.
