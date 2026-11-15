// covers: LANG-721, LANG-725, CMP-380
program AnonSelfTwoLevels;

type
  TProc = reference to procedure;
  TMaker = reference to function: TProc;

  TBox = class
    FValue: Integer;
    FName: string;
    function Maker: TMaker;
    procedure Show;
  end;

procedure TBox.Show;
begin
  Writeln(FName, '=', FValue);
end;

function TBox.Maker: TMaker;
begin
  Result := function: TProc
    begin
      FValue := FValue + 1;
      Result := procedure
        begin
          FValue := FValue * 10;
          Show;
          Self.FName := FName + '!';
        end;
    end;
end;

procedure Run;
var
  B: TBox;
  M: TMaker;
  P: TProc;
begin
  B := TBox.Create;
  B.FName := 'b';
  B.FValue := 1;
  M := B.Maker;
  P := M();
  P();
  P();
  Writeln(B.FName, ' ', B.FValue);
  M := nil;
  P := nil;
  B.Free;
end;

var
  Before: Integer;
begin
  Before := _LiveAllocCount;
  Run;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
