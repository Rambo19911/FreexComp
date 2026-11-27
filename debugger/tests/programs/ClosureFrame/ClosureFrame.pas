program ClosureFrame;

type
  TIntFunc = reference to function(Delta: Integer): Integer;
  TProc = reference to procedure;

  TCounter = class
    FName: string;
    function MakeAdder(Start: Integer): TIntFunc;
  end;

function TCounter.MakeAdder(Start: Integer): TIntFunc;
var
  Count: Integer;

  procedure Reset; // uses the closure: the method's frame record holds it
  begin
    Count := 0;
  end;

begin
  Reset;
  Result := function(Delta: Integer): Integer
    var
      Twice: Integer;
      Log: TProc;

      procedure Bump; // uses the frame parameter of the anonymous function
      begin
        Inc(Count);
      end;

    begin
      Twice := Delta * 2;
      Bump;
      Start := Start + Twice; // stop A
      Log := procedure
        begin
          WriteLn(FName, ' ', Count, ' ', Start, ' ', Twice); // stop C
        end;
      Log();
      Result := Start;
    end;
  Result(5);
  WriteLn(Count, ' ', Start); // stop B
end;

var
  Counter: TCounter;
  Adder: TIntFunc;
begin
  Counter := TCounter.Create;
  Counter.FName := 'ctr';
  Adder := Counter.MakeAdder(10);
  WriteLn(Adder(1));
  Counter.Free;
end.
