// covers: LANG-422, LANG-442
program DefaultParams;

type
  TColor = (Red, Green, Blue);
  TColors = set of TColor;
  TThing = class
    function Describe(const Prefix: string = 'thing'; Count: Integer = 1): string;
  end;

const
  DefaultWidth = 8;

function TThing.Describe(const Prefix: string; Count: Integer): string;
begin
  Result := Prefix;
  while Count > 1 do
  begin
    Result := Result + '+' + Prefix;
    Dec(Count);
  end;
end;

procedure Show(I: Integer; Width: Integer = DefaultWidth; const Sep: string = '|'; C: Char = '*';
  Flag: Boolean = True; R: Double = 0.5; Color: TColor = Green);
begin
  Writeln(I, Sep, Width, Sep, C, Sep, Flag, Sep, R:0:2, Sep, Ord(Color));
end;

procedure Kinds(Colors: TColors = [Red, Blue]; P: Pointer = nil; O: TThing = nil);
begin
  Writeln(Red in Colors, ' ', Green in Colors, ' ', Blue in Colors, ' ', P = nil, ' ', O = nil);
end;

function Add(A: Integer; B: Integer = 10): Integer; overload;
begin
  Result := A + B;
end;

function Add(const A: string; const B: string = '!'): string; overload;
begin
  Result := A + B;
end;

procedure G(A: Integer; B: Byte = 0); overload;
begin
  Writeln('G fewer defaults');
end;

procedure G(A: Integer; B: Word; C: Integer = 0); overload;
begin
  Writeln('G more defaults');
end;

procedure Pick(A: Integer; B: Int64 = 0); overload;
begin
  Writeln('Pick two ', B);
end;

procedure Pick(A: Int64; B: Int64; C: Int64 = 0); overload;
begin
  Writeln('Pick three ', C);
end;

procedure Forwarded(X: Integer = 3); forward;

procedure Forwarded(X: Integer);
begin
  Writeln('Forwarded ', X);
end;

var
  T: TThing;
begin
  Show(1);
  Show(2, 3);
  Show(3, 4, ', ');
  Show(4, 5, '-', '#', False, 1.25, Blue);
  Kinds;
  Kinds([]);
  Writeln(Add(1));
  Writeln(Add(1, 2));
  Writeln(Add('hi'));
  Writeln(Add('hi', '?'));
  Pick(1, 2);
  Pick(1, 2, 3);
  G(1, 1);
  Forwarded;
  Forwarded(4);
  T := TThing.Create;
  Writeln(T.Describe);
  Writeln(T.Describe('x'));
  Writeln(T.Describe('y', 3));
  T.Free;
end.
