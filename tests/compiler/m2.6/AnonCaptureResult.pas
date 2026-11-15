// covers: LANG-721, CMP-380
program AnonCaptureResult;

type
  TProc = reference to procedure;
  TStrProc = reference to procedure(const S: string);

  TBig = record
    A, B, C: Integer;
  end;

  TFoo = class
    FValue: Integer;
    function Twice: Integer;
  end;

function Counted: Integer;
var
  P: TProc;
begin
  Result := 1;
  P := procedure begin Result := Result * 10 end;
  P();
  P();
end;

function Joined(const A, B: string): string;
var
  Add: TStrProc;
begin
  Result := '';
  Add := procedure(const S: string) begin Result := Result + S + ';' end;
  Add(A);
  Add(B);
end;

function Early(N: Integer): Integer;
var
  P: TProc;
begin
  Result := -1;
  P := procedure begin Result := N * 2 end;
  if N > 0 then
  begin
    P();
    Exit;
  end;
  Result := 0;
end;

function Keeper(var P: TProc): Integer;
begin
  Result := 7;
  P := procedure
    begin
      Writeln('kept ', Result);
      Result := Result + 1;
    end;
end;

function MakeBig: TBig;
var
  P: TProc;
begin
  Result.A := 1;
  P := procedure
    begin
      Result.B := Result.A + 1;
      Result.C := 3;
    end;
  P();
end;

function TFoo.Twice: Integer;
var
  P: TProc;
begin
  Result := FValue;
  P := procedure begin Result := Result * 2 end;
  P();
end;

procedure Run;
var
  P: TProc;
  Big: TBig;
  Foo: TFoo;
  S: string;
begin
  Writeln(Counted);
  S := Joined('x', 'y');
  Writeln(S);
  Writeln(Early(4), ' ', Early(0));
  Writeln(Keeper(P));
  P();
  P();
  P := nil;
  Big := MakeBig;
  Writeln(Big.A, ' ', Big.B, ' ', Big.C);
  Foo := TFoo.Create;
  Foo.FValue := 21;
  Writeln(Foo.Twice);
  Foo.Free;
end;

var
  Before: Integer;
begin
  Before := _LiveAllocCount;
  Run;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
