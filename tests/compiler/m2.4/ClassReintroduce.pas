// covers: LANG-604, LANG-622
program ClassReintroduce;

type
  TBase = class
    procedure Show; virtual;
    function Name: string; virtual;
  end;

  THider = class(TBase)
    procedure Show;
    function Name: string; reintroduce; virtual;
  end;

  TNext = class(THider)
    function Name: string; override;
  end;

procedure TBase.Show;
begin
  Writeln('base show');
end;

function TBase.Name: string;
begin
  Result := 'base';
end;

procedure THider.Show;
begin
  Writeln('hider show');
end;

function THider.Name: string;
begin
  Result := 'hider';
end;

function TNext.Name: string;
begin
  Result := 'next';
end;

var
  B: TBase;
  H: THider;
begin
  H := THider.Create;
  B := H;
  B.Show;
  H.Show;
  Writeln(B.Name, ' ', H.Name);
  H.Free;
  H := TNext.Create;
  B := H;
  Writeln(B.Name, ' ', H.Name);
  B.Show;
  H.Free;
end.
