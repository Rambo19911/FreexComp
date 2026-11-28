// covers: LANG-992, LANG-994
program HelperLookup;

type
  TBox = class
    function Size: Integer;
    function Name: string;
    procedure Show;
    procedure Run;
  end;
  TBoxClass = class of TBox;
  TBig = class(TBox)
  end;

  TBoxHelper = class helper for TBox
    class var Count: Integer;
    function Twice: Integer;
    procedure Show(const Prefix: string); overload;
    function Name: string;
    class function Kind: string; static;
    procedure Grow(N: Integer); overload;
  end;

  // an heir of the helper adds an overload: the chain is one overload set
  TBoxHelper2 = class helper(TBoxHelper) for TBox
    procedure Grow(const S: string); overload;
  end;

  TPair = record
    A, B: Integer;
  end;
  TPairHelper = record helper for TPair
    function Sum: Integer;
  end;

  TIntFunc = function: Integer of object;
  TStrFunc = function: string;
  TIntRef = reference to function: Integer;

function TBox.Size: Integer;
begin
  Result := 1;
end;

function TBox.Name: string;
begin
  Result := 'box';
end;

procedure TBox.Show;
begin
  Writeln('box');
end;

function TBoxHelper.Twice: Integer;
begin
  Result := Size * 2;
end;

procedure TBoxHelper.Show(const Prefix: string);
begin
  Writeln(Prefix, 'box');
end;

function TBoxHelper.Name: string;
begin
  Result := 'helped';
end;

class function TBoxHelper.Kind: string;
begin
  Result := 'kind';
end;

procedure TBoxHelper.Grow(N: Integer);
begin
  Writeln('grow ', N);
end;

procedure TBoxHelper2.Grow(const S: string);
begin
  Writeln('grow ', S);
end;

function TPairHelper.Sum: Integer;
begin
  Result := A + B;
end;

// implicit Self in a method of the helped class: the helper's members, its Name before the class's
procedure TBox.Run;
begin
  Writeln('run twice ', Twice);
  Writeln('run name ', Name);
end;

var
  B: TBox;
  Big: TBig;
  C: TBoxClass;
  P: TPair;
  K: TStrFunc;
  M: TIntFunc;
  R: TIntRef;
begin
  B := TBox.Create;
  Big := TBig.Create;
  B.Run;
  with B do
    Writeln('with class ', Twice);
  P.A := 3;
  P.B := 4;
  with P do
    Writeln('with record ', Sum);
  // one overload set from the class and the helper
  B.Show;
  B.Show('my ');
  B.Grow(3);
  B.Grow('x');
  Writeln('ancestor level ', Big.Twice);
  C := TBig;
  Writeln('class reference ', C.Kind);
  Writeln('helper name ', TBoxHelper.Kind);
  // the helper's class var is one variable, named through the helper or the helped class
  Inc(TBoxHelper.Count);
  Writeln('class var ', TBox.Count);
  K := TBox.Kind;
  Writeln('procedural ', K());
  M := B.Twice;
  Writeln('method pointer ', M());
  R := B.Twice;
  Writeln('method reference ', R());
  Big.Free;
  B.Free;
end.
