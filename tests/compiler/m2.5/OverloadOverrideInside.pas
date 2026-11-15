// covers: LANG-441, LANG-442, LANG-310 (C-210)
program OverloadOverrideInside;

{ Inside a method, a bare call of an overloaded name whose parameterless overload overrides an inherited virtual
  method binds like Self.Name: the overridden ancestor method is not a second candidate, and the call is virtual. }

type
  TA = class
    function ToString: string; override;
  end;

  TB = class(TA)
  public
    function ToString: string; overload; override;
    function ToString(N: Integer): string; reintroduce; overload;
    function Twice: string;
  end;

  TC = class(TB)
    function ToString: string; override;
  end;

function TA.ToString: string;
begin
  Result := 'a';
end;

function TB.ToString: string;
begin
  Result := 'b';
end;

function TB.ToString(N: Integer): string;
begin
  Result := 'n' + Chr(Ord('0') + N);
end;

function TB.Twice: string;
begin
  Result := ToString + ToString() + ToString(1) + Self.ToString;
end;

function TC.ToString: string;
begin
  Result := 'c';
end;

var
  B: TB;
begin
  B := TB.Create;
  Writeln(B.ToString, ' ', B.Twice);
  B.Free;
  B := TC.Create;
  Writeln(B.ToString, ' ', B.Twice);
  B.Free;
end.
