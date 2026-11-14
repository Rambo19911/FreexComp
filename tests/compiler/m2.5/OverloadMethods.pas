// covers: LANG-441, LANG-604
program OverloadMethods;

type
  TBase = class
    procedure Put(X: Integer); overload;
    procedure Put(const X: string); overload;
    procedure Show(X: Integer); overload; virtual;
    procedure Show(const X: string); overload; virtual;
    procedure Run;
  end;

  TDerived = class(TBase)
    procedure Put(X: Double); overload;
    procedure Show(X: Integer); overload; override;
  end;

procedure TBase.Put(X: Integer);
begin
  Writeln('TBase.Put Integer ', X);
end;

procedure TBase.Put(const X: string);
begin
  Writeln('TBase.Put string ', X);
end;

procedure TBase.Show(X: Integer);
begin
  Writeln('TBase.Show Integer ', X);
end;

procedure TBase.Show(const X: string);
begin
  Writeln('TBase.Show string ', X);
end;

procedure TBase.Run;
begin
  Show(1);
  Show('run');
end;

procedure TDerived.Put(X: Double);
begin
  Writeln('TDerived.Put Double');
end;

procedure TDerived.Show(X: Integer);
begin
  Writeln('TDerived.Show Integer ', X);
  inherited Show(X + 1);
end;

var
  B: TBase;
  D: TDerived;
begin
  D := TDerived.Create;
  D.Put(1);
  D.Put(1.5);
  D.Put('x');
  D.Show(5);
  D.Show('y');
  B := D;
  B.Put(2);
  B.Show(7);
  B.Run;
  D.Free;
  B := TBase.Create;
  B.Run;
  B.Free;
end.
