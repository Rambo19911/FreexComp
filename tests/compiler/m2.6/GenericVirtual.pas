// covers: LANG-717
program GenericVirtual;

type
  TShape<T> = class
  protected
    FValue: T;
  public
    constructor Create(const AValue: T);
    function Describe: string; virtual;
    function Value: T;
  end;

  TLabeled<T> = class(TShape<T>)
  private
    FLabel: string;
  public
    constructor Create(const ALabel: string; const AValue: T);
    function Describe: string; override;
  end;

  TIntBox = class(TShape<Integer>)
    function Describe: string; override;
  end;

constructor TShape<T>.Create(const AValue: T);
begin
  inherited Create;
  FValue := AValue;
end;

function TShape<T>.Describe: string;
begin
  Result := 'shape';
end;

function TShape<T>.Value: T;
begin
  Result := FValue;
end;

constructor TLabeled<T>.Create(const ALabel: string; const AValue: T);
begin
  inherited Create(AValue);
  FLabel := ALabel;
end;

function TLabeled<T>.Describe: string;
begin
  Result := FLabel + ':' + inherited Describe;
end;

function TIntBox.Describe: string;
begin
  Result := 'box';
end;

var
  S: TShape<Integer>;
  T: TShape<string>;
begin
  S := TShape<Integer>.Create(1);
  Writeln(S.Describe, ' ', S.Value);
  S.Free;
  S := TLabeled<Integer>.Create('lbl', 2);
  Writeln(S.Describe, ' ', S.Value);
  S.Free;
  S := TIntBox.Create(3);
  Writeln(S.Describe, ' ', S.Value, ' ', S is TIntBox);
  S.Free;
  T := TLabeled<string>.Create('x', 'y');
  Writeln(T.Describe, ' ', T.Value);
  T.Free;
end.