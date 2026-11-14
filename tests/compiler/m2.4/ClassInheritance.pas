// covers: LANG-604, LANG-621, LANG-624
program ClassInheritance;

{ Inherited fields used in descendant methods; inherited Name(args) calling the ancestor statically (also of a
  virtual method, through a middle class that does not override it); bare 'inherited' passing the current parameters
  (const string included), doing nothing without an ancestor method, and in a function assigning nothing;
  Result := inherited Name(args); static methods hidden in descendants. }

type
  TBase = class
    Value: Integer;
    procedure SetValue(V: Integer); virtual;
    function Calc(X: Integer): Integer; virtual;
    function Twice: Integer; virtual;
    procedure Hello; virtual;
    procedure Log(const Msg: string; N: Integer);
  end;

  TMid = class(TBase)
    procedure SetValue(V: Integer); override;
    procedure Log(const Msg: string; N: Integer);
  end;

  TTop = class(TMid)
    Extra: Integer;
    procedure SetValue(V: Integer); override;
    function Calc(X: Integer): Integer; override;
    function Twice: Integer; override;
    procedure Hello; override;
    procedure Bye;
    procedure Log(const Msg: string; N: Integer);
  end;

procedure TBase.SetValue(V: Integer);
begin
  Writeln('base set ', V);
  Value := V;
end;

function TBase.Calc(X: Integer): Integer;
begin
  Result := X + Value;
end;

function TBase.Twice: Integer;
begin
  Writeln('base twice');
  Result := Value * 2;
end;

procedure TBase.Hello;
begin
  Writeln('hello from base');
end;

procedure TBase.Log(const Msg: string; N: Integer);
begin
  Writeln('base log ', Msg, ' ', N);
end;

procedure TMid.SetValue(V: Integer);
begin
  Writeln('mid set ', V);
  inherited SetValue(V * 2);
end;

procedure TMid.Log(const Msg: string; N: Integer);
begin
  Writeln('mid log ', Msg);
  inherited;
end;

procedure TTop.SetValue(V: Integer);
begin
  Writeln('top set ', V);
  inherited;
  Extra := Value + 1;
end;

function TTop.Calc(X: Integer): Integer;
begin
  Result := inherited Calc(X) * 10;
end;

function TTop.Twice: Integer;
begin
  Result := 100;
  inherited;
end;

procedure TTop.Hello;
begin
  inherited;
  inherited Hello;
  Writeln('hello from top');
end;

procedure TTop.Bye;
begin
  inherited;
  Writeln('bye, value ', Value, ' extra ', Extra);
end;

procedure TTop.Log(const Msg: string; N: Integer);
begin
  inherited Log(Msg + '!', N + 1);
end;

var
  B: TBase;
  T: TTop;
begin
  T := TTop.Create;
  B := T;
  B.SetValue(3);
  Writeln(T.Value, ' ', T.Extra);
  Writeln(B.Calc(1));
  B.Hello;
  T.Bye;
  T.Log('x', 1);
  B.Log('y', 5);
  TMid(B).Log('z', 0);
  Writeln(B.Twice);
  T.Free;
end.
