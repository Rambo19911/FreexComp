// covers: LANG-615, LANG-620, LANG-647, LANG-653, CMP-367, CMP-370
program ClassMethodsStatic;

{ class var (one storage shared with descendants, a managed one included), static class methods called on the class
  and on an instance, a class property with static accessors and one with a class var accessor, a virtual class
  method dispatched from an instance method and on an instance, inherited in a class method, and a constructor called
  inside a class method (it allocates an instance of Self's run-time class). }

type
  TCounter = class
  private
    class var FCount: Integer;
    class var FTitle: string;
    class function GetCount: Integer; static;
    class procedure SetCount(Value: Integer); static;
  public
    Id: Integer;
    constructor Create;
    class function Describe: string; virtual;
    class procedure Reset; static;
    class function Twice(N: Integer): Integer; static;
    class function Make: TCounter;
    procedure Show;
    class property Count: Integer read GetCount write SetCount;
    class property Title: string read FTitle write FTitle;
  end;

  TSpecial = class(TCounter)
    class function Describe: string; override;
  end;

constructor TCounter.Create;
begin
  inherited Create;
  FCount := FCount + 1;
  Id := FCount;
end;

class function TCounter.GetCount: Integer;
begin
  Result := FCount;
end;

class procedure TCounter.SetCount(Value: Integer);
begin
  Writeln('count := ', Value);
  FCount := Value;
end;

class function TCounter.Describe: string;
begin
  Result := 'counter';
end;

class procedure TCounter.Reset;
begin
  FCount := 0;
  FTitle := 'reset';
end;

class function TCounter.Twice(N: Integer): Integer;
begin
  Result := N * 2;
end;

class function TCounter.Make: TCounter;
begin
  Result := Create;
end;

procedure TCounter.Show;
begin
  Writeln(Describe, ' #', Id, ' of ', Count);
end;

class function TSpecial.Describe: string;
begin
  Result := 'special ' + inherited Describe;
end;

var
  A, B, S: TCounter;
begin
  Writeln(TCounter.Count);
  A := TCounter.Create;
  B := TCounter.Create;
  Writeln(TCounter.Count, ' ', A.Count, ' ', B.Id);
  TCounter.Count := 10;
  S := TSpecial.Make;
  S.Show;
  A.Show;
  Writeln(TCounter.Describe, ' ', TSpecial.Describe, ' ', S.Describe);
  Writeln(TCounter.Twice(21), ' ', A.Twice(4));
  TCounter.Title := 'hello';
  Writeln(TSpecial.Title);
  A.Reset;
  Writeln(TCounter.Count, ' ', TCounter.Title);
  A.Free;
  B.Free;
  S.Free;
end.
