// covers: LANG-622
program ErrOverrideMissing;

type
  TBase = class
    procedure Draw; virtual;
    function Size: Integer; virtual;
    procedure Fixed;
  end;

  TDerived = class(TBase)
    procedure Paint; override;
    function Size(Scale: Integer): Integer; override;
    procedure Fixed; override;
  end;

procedure TBase.Draw;
begin
end;

function TBase.Size: Integer;
begin
  Result := 1;
end;

procedure TBase.Fixed;
begin
end;

procedure TDerived.Paint;
begin
end;

function TDerived.Size(Scale: Integer): Integer;
begin
  Result := Scale;
end;

procedure TDerived.Fixed;
begin
end;

begin
end.
