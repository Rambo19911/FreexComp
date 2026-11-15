// covers: LANG-442, LANG-480, LANG-481 (C-221)
program OverloadMethodPointerArg;

type
  TM = procedure of object;
  TP = procedure;

  TC = class
    Name: string;
    procedure Run;
    procedure One(M: TM);
    procedure Two(M: TM); overload;
    procedure Two(A: TObject; M: TM); overload;
    procedure Two(const S: string); overload;
    function Self2: TObject;
  end;

procedure Plain;
begin
  Writeln('plain');
end;

procedure Call(P: TP); overload;
begin
  P;
end;

procedure Call(N: Integer); overload;
begin
  Writeln('int ', N);
end;

procedure TC.Run;
begin
  Writeln('run ', Name);
end;

procedure TC.One(M: TM);
begin
  M;
end;

procedure TC.Two(M: TM);
begin
  Write('two: ');
  M;
end;

procedure TC.Two(A: TObject; M: TM);
begin
  Write('two(', A = nil, '): ');
  M;
end;

procedure TC.Two(const S: string);
begin
  Writeln('two ', S);
end;

function TC.Self2: TObject;
begin
  Result := Self;
end;

var
  C: TC;
begin
  C := TC.Create;
  C.Name := 'c';
  C.One(C.Run);
  C.Two(C.Run);
  C.Two(nil, C.Run);
  C.Two(C.Self2, C.Run);
  C.Two('s');
  Call(Plain);
  Call(3);
  C.Free;
end.