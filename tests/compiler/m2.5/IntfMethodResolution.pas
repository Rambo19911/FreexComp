// covers: LANG-661
program IntfMethodResolution;

{ Method resolution clauses map interface methods to differently named methods; two interfaces with a method of the
  same name (Reset) get different implementations. The mapped methods stay callable directly. }

type
  IReader = interface
    ['{7E7E0001-0000-0000-0000-000000000001}']
    function Next: Integer;
    procedure Reset;
  end;

  IWriter = interface
    ['{7E7E0001-0000-0000-0000-000000000002}']
    procedure Reset;
    procedure Put(V: Integer);
  end;

  TBuffer = class(TInterfacedObject, IReader, IWriter)
  private
    FPos, FLast: Integer;
  public
    function IReader.Next = ReadNext;
    procedure IReader.Reset = ResetRead;
    procedure IWriter.Reset = ResetWrite;
    function ReadNext: Integer;
    procedure ResetRead;
    procedure ResetWrite;
    procedure Put(V: Integer);
  end;

function TBuffer.ReadNext: Integer;
begin
  Inc(FPos);
  Result := FPos * 10 + FLast;
end;

procedure TBuffer.ResetRead;
begin
  Writeln('reset read');
  FPos := 0;
end;

procedure TBuffer.ResetWrite;
begin
  Writeln('reset write');
  FLast := 0;
end;

procedure TBuffer.Put(V: Integer);
begin
  FLast := V;
end;

var
  B: TBuffer;
  R: IReader;
  W: IWriter;
begin
  B := TBuffer.Create;
  R := B;
  W := B;
  W.Put(3);
  Writeln(R.Next);
  Writeln(R.Next);
  R.Reset;
  Writeln(R.Next);
  W.Reset;
  Writeln(R.Next);
  B.ResetRead;
  Writeln(B.ReadNext);
  R := nil;
  W := nil;
  Writeln('done');
end.
