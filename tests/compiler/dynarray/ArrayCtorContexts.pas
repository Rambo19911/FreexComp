// covers: LANG-270, LANG-271, LANG-272, LANG-273, CMP-1415
program ArrayCtorContexts;

{ Array constructors in every dynamic-array context: D.Create(...) of a named type and of a generic instance (also
  without a context, typing an inline variable), the cast D([...]), field and property assignment, an element, an
  inline variable with a type, an overloaded call that takes the constructor as a dynamic array, nested constructors.
  An array of interfaces built by a constructor and by Create releases every instance (counter 0 after the routine). }

type
  TInts = array of Integer;
  TGrid = array of TInts;
  TStrs = array of string;

  IThing = interface
    function Id: Integer;
  end;

  TThing = class(TInterfacedObject, IThing)
  private
    FId: Integer;
  public
    constructor Create(AId: Integer);
    destructor Destroy; override;
    function Id: Integer;
  end;

  TThings = array of IThing;

  THolder = record
    Values: TInts;
  end;

  TBox = class
  private
    FItems: TInts;
    FNames: TStrs;
    procedure SetNames(const Value: TStrs);
  public
    property Items: TInts read FItems write FItems;
    property Names: TStrs read FNames write SetNames;
  end;

var
  Live: Integer;

constructor TThing.Create(AId: Integer);
begin
  inherited Create;
  FId := AId;
  Inc(Live);
end;

destructor TThing.Destroy;
begin
  Dec(Live);
  inherited Destroy;
end;

function TThing.Id: Integer;
begin
  Result := FId;
end;

procedure TBox.SetNames(const Value: TStrs);
begin
  FNames := Value;
  Writeln('names set: ', Length(Value));
end;

procedure Show(const Title: string; const A: TInts);
var
  K: Integer;
begin
  Write(Title, ':');
  for K := 0 to High(A) do
    Write(' ', A[K]);
  Writeln(' (', Length(A), ')');
end;

procedure P(I: Integer); overload;
begin
  Writeln('P(Integer) ', I);
end;

procedure P(const A: TInts); overload;
begin
  Show('P(TInts)', A);
end;

procedure Things;
var
  T: TThings;
begin
  T := [TThing.Create(1), TThing.Create(2)];
  Writeln('things ', Length(T), ' ids ', T[0].Id, T[1].Id);
  T := TThings.Create(TThing.Create(3), TThing.Create(4), TThing.Create(5));
  Writeln('things ', Length(T), ' ids ', T[0].Id, T[1].Id, T[2].Id);
  T := nil;
end;

procedure Run;
var
  A: TInts;
  G: TGrid;
  Arr: TArray<Integer>;
  H: THolder;
  Box: TBox;
  N: Integer;
begin
  N := 7;
  A := TInts.Create(1, 2, N);
  Show('Create', A);
  A := TInts.Create;
  Show('Create empty', A);
  Writeln('assigned ', Assigned(A));
  Arr := TArray<Integer>.Create(10, 20);
  Writeln('TArray ', Length(Arr), ' ', Arr[0] + Arr[1]);
  var Inferred := TInts.Create(4, 5, 6);
  Show('inferred', Inferred);
  Writeln('length of a value ', Length(TInts.Create(9, 9, 9, 9)));
  A := TInts([3, 2, 1]);
  Show('cast', A);
  H.Values := [11, 12];
  Show('field', H.Values);
  Box := TBox.Create;
  Box.Items := [21, 22, 23];
  Show('property', Box.Items);
  Box.Names := ['x', 'y'];
  Writeln('names ', Box.Names[0], Box.Names[1]);
  Box.Free;
  G := TGrid.Create([1], [], [2, 3]);
  Writeln('grid ', Length(G), ' ', Length(G[0]), Length(G[1]), Length(G[2]), ' ', G[2][1]);
  G[1] := [N, N];
  Show('element', G[1]);
  var Typed: TInts := [8, N];
  Show('typed inline', Typed);
  P([1, 2]);
  P(5);
  P(TInts.Create(6));
  G := [[1, 2], TInts.Create(3), TInts([4])];
  Writeln('nested ', G[0][1], G[1][0], G[2][0]);
end;

begin
  Run;
  Things;
  { managed temporaries are released at routine exit (CMP-325 rule 5), so the count is read after the call }
  Writeln('live at end: ', Live);
end.
