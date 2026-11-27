{ context: override }
program OverrideProcedure;
type
  TBase = class
    procedure Open; virtual;
    procedure Close; virtual;
    procedure Plain;
  end;
  TDerived = class(TBase)
    procedure Open; override;
    procedure {|}
  end;
procedure TBase.Open; begin end;
procedure TBase.Close; begin end;
procedure TBase.Plain; begin end;
procedure TDerived.Open; begin end;
begin
end.
{#has Close, Destroy}
{#kind Close=Method}
{#not Open, Plain}
