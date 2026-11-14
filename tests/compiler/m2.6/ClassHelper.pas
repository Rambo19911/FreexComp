// covers: LANG-750, LANG-751, LANG-752
program ClassHelper;

type
  TCounter = class
  private
    FCount: Integer;
  public
    procedure Add(N: Integer);
    function Describe: string;
  end;

  TCounterHelper = class helper for TCounter
  public
    const Title = 'helper';
    procedure AddTwice(N: Integer);
    function Describe: string;
    function Doubled: Integer;
    class function Kind: string; static;
    property Twice: Integer read Doubled;
  end;

  TChild = class(TCounter)
  end;

procedure TCounter.Add(N: Integer);
begin
  Inc(FCount, N);
end;

function TCounter.Describe: string;
begin
  Result := 'counter';
end;

procedure TCounterHelper.AddTwice(N: Integer);
begin
  Add(N);
  Self.Add(N);
end;

function TCounterHelper.Describe: string;
begin
  Result := 'helped ' + Title;
end;

function TCounterHelper.Doubled: Integer;
begin
  Result := FCount * 2;
end;

class function TCounterHelper.Kind: string;
begin
  Result := 'kind';
end;

var
  C: TCounter;
  D: TChild;
begin
  C := TCounter.Create;
  C.Add(1);
  C.AddTwice(3);
  Writeln(C.Doubled, ' ', C.Twice);
  Writeln(C.Describe);
  Writeln(TCounter.Kind);
  D := TChild.Create;
  D.AddTwice(5);
  Writeln(D.Doubled, ' ', D.Describe);
  C.Free;
  D.Free;
end.
