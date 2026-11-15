// covers: LANG-721, LANG-725, CMP-345, CMP-380
program AnonMixedCapture;

type
  TProc = reference to procedure;
  TIntFunc = reference to function: Integer;

function Direct: Integer;
var
  X: Integer;
  P: TProc;

  procedure Bump;
  begin
    X := X + 100;
  end;

begin
  X := 1;
  P := procedure begin X := X + 10 end;
  Bump;
  P();
  Bump;
  Result := X;
end;

function ThroughNested: TIntFunc;
var
  Base: Integer;

  function Make(Step: Integer): TIntFunc;
  begin
    Result := function: Integer
      begin
        Base := Base + Step;
        Result := Base;
      end;
  end;

begin
  Base := 40;
  Result := Make(5);
  Base := 50;
end;

function NestedInAnon: Integer;
var
  Total: Integer;
  P: TProc;
begin
  Total := 5;
  P := procedure
    var
      Times: Integer;

    procedure AddOne;
    begin
      Total := Total + 1;
      Times := Times + 1;
    end;

    begin
      Times := 0;
      AddOne;
      AddOne;
      Writeln('times ', Times);
    end;
  P();
  Result := Total;
end;

function Deep: TIntFunc;
var
  Seed: Integer;

  function Outer1: TIntFunc;

    function Inner: TIntFunc;
    begin
      Result := function: Integer
        begin
          Seed := Seed * 2;
          Result := Seed;
        end;
    end;

  begin
    Result := Inner;
  end;

begin
  Seed := 3;
  Result := Outer1;
end;

procedure WithFinally;
var
  Msg: string;
  P: TProc;
begin
  Msg := 'a';
  P := procedure begin Msg := Msg + 'b' end;
  try
    P();
  finally
    Writeln('finally ', Msg);
  end;
end;

procedure Run;
var
  F: TIntFunc;
begin
  Writeln(Direct);
  F := ThroughNested;
  Writeln(F(), ' ', F());
  Writeln(NestedInAnon);
  F := Deep;
  Writeln(F(), ' ', F());
  F := nil;
  WithFinally;
end;

var
  Before: Integer;
begin
  Before := _LiveAllocCount;
  Run;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
