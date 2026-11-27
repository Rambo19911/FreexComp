// covers: LANG-970, LANG-725
program ErrAnonCapture;

type
  TProc = reference to procedure;

  TRec = record
    Value: Integer;
    class var Count: Integer;
    procedure M;
    procedure N;
    procedure Counted;
  end;

  TObj = class
    FValue: Integer;
    procedure Run;
  end;

// row 1: a var parameter
procedure P1(var X: Integer);
var
  F: TProc;
begin
  F := procedure begin X := 1 end;
  F;
end;

// row 2: an out parameter
procedure P2(out X: Integer);
var
  F: TProc;
begin
  X := 0;
  F := procedure begin X := 2 end;
  F;
end;

// row 3: an open-array parameter
procedure P3(A: array of Integer);
var
  F: TProc;
begin
  F := procedure begin Writeln(A[0]) end;
  F;
end;

// row 4: a const open-array parameter; its Length is read at run time
procedure P4(const A: array of string);
var
  F: TProc;
begin
  F := procedure begin Writeln(Length(A)) end;
  F;
end;

// row 5: an untyped parameter
procedure P5(const X);
var
  F: TProc;
begin
  F := procedure begin Writeln(@X = nil) end;
  F;
end;

// row 6: named twice in one anonymous method, reported once at the first
procedure P6(var X: Integer);
var
  F: TProc;
begin
  F := procedure begin X := X + 1 end;
  F;
end;

// row 7: named only by the inner anonymous method, reported once
procedure P7(var X: Integer);
var
  F: TProc;
begin
  F := procedure
    var
      G: TProc;
    begin
      G := procedure begin X := 7 end;
      G;
    end;
  F;
end;

// row 8: two anonymous-method expressions, reported for each
procedure P8(var X: Integer);
var
  F, G: TProc;
begin
  F := procedure begin X := 8 end;
  G := procedure begin X := 9 end;
  F;
  G;
end;

// row 9: Self of a record method, implied by a field
procedure TRec.M;
var
  F: TProc;
begin
  F := procedure begin Writeln(Value) end;
  F;
end;

// row 10: Self of a record method, written
procedure TRec.N;
var
  F: TProc;
begin
  F := procedure begin Writeln(Self.Value) end;
  F;
end;

// row 11: a nested procedure is no method reference
procedure P11;
var
  F: TProc;

  procedure Inner;
  begin
  end;

begin
  F := Inner;
  F;
end;

// row 12, no diagnostic: value and const parameters are copied; SizeOf evaluates nothing
procedure P12(V: Integer; const S: string; const R: TRec; var X: Integer);
var
  F: TProc;
begin
  F := procedure begin Writeln(V, S, R.Value, SizeOf(X)) end;
  F;
end;

// row 12, no diagnostic: a class var of the record needs no Self
procedure TRec.Counted;
var
  F: TProc;
begin
  F := procedure begin Count := Count + 1 end;
  F;
end;

// row 12, no diagnostic: Self and a field in a method of a class
procedure TObj.Run;
var
  F: TProc;
begin
  F := procedure begin FValue := FValue + 1; Writeln(Self.FValue) end;
  F;
end;

begin
end.
