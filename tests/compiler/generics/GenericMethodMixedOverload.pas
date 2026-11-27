// covers: LANG-980, LANG-978, LANG-442
program GenericMethodMixedOverload;

type
  TOut = class
    class procedure Show(I: Integer); overload; static;
    class procedure Show<T>(const V: T); overload; static;
  end;

  TCounter = class
    Calls: Integer;
    procedure Put(I: Integer); overload;
    procedure Put<T>(const V: T); overload;
    class procedure Mark(I: Integer); overload;
    class procedure Mark<T>(const V: T); overload;
    procedure Run;
  end;

  TCounterClass = class of TCounter;

  TPoint = record
    X: Integer;
    procedure Add(I: Integer); overload;
    procedure Add<T>(const V: T); overload;
    class procedure Show(I: Integer); overload; static;
    class procedure Show<T>(const V: T); overload; static;
    procedure Run;
  end;

  TBase = class
    class procedure Log<T>(const V: T); overload; static;
  end;

  TDerived = class(TBase)
    class procedure Log(I: Integer); overload; static;
  end;

  TProbe = class
    class procedure Take(I: Integer); overload; static;
    class procedure Take<T>(const V: T); overload; static;
  end;

{ Which instance ran: the kind and the size of its type argument. }
function Describe(Kind: TTypeKind; Size: Integer): string;
begin
  case Kind of
    tkInteger: Result := 'an integer';
    tkFloat: Result := 'a real';
    tkUString: Result := 'a string';
    tkWChar: Result := 'a character';
    tkClass: Result := 'an object';
  else
    Result := 'something else';
  end;
  if Size = 1 then
    Result := Result + ' of 1 byte'
  else
    Result := Result + ' of ' + Chr(Ord('0') + Size) + ' bytes';
end;

class procedure TOut.Show(I: Integer);
begin
  Writeln('TOut.Show(Integer): ', I);
end;

class procedure TOut.Show<T>(const V: T);
begin
  Writeln('TOut.Show<T>: ', Describe(GetTypeKind(T), SizeOf(T)));
end;

procedure TCounter.Put(I: Integer);
begin
  Inc(Calls);
  Writeln('TCounter.Put(Integer): ', I, ', call ', Calls);
end;

procedure TCounter.Put<T>(const V: T);
begin
  Inc(Calls);
  Writeln('TCounter.Put<T>: ', Describe(GetTypeKind(T), SizeOf(T)), ', call ', Calls);
end;

class procedure TCounter.Mark(I: Integer);
begin
  Writeln(ClassName, '.Mark(Integer): ', I);
end;

class procedure TCounter.Mark<T>(const V: T);
begin
  Writeln(ClassName, '.Mark<T>: ', Describe(GetTypeKind(T), SizeOf(T)));
end;

procedure TCounter.Run;
begin
  Put(1);
  Put('one');
  Put(1.5);
  Put<Integer>(1);
  Mark(2);
  Mark('two');
end;

procedure TPoint.Add(I: Integer);
begin
  X := X + I;
  Writeln('TPoint.Add(Integer): ', I, ', X = ', X);
end;

procedure TPoint.Add<T>(const V: T);
begin
  X := X + 100;
  Writeln('TPoint.Add<T>: ', Describe(GetTypeKind(T), SizeOf(T)), ', X = ', X);
end;

class procedure TPoint.Show(I: Integer);
begin
  Writeln('TPoint.Show(Integer): ', I);
end;

class procedure TPoint.Show<T>(const V: T);
begin
  Writeln('TPoint.Show<T>: ', Describe(GetTypeKind(T), SizeOf(T)));
end;

procedure TPoint.Run;
begin
  Add(3);
  Add('three');
  Show(4);
  Show(4.5);
end;

class procedure TBase.Log<T>(const V: T);
begin
  Writeln('TBase.Log<T>: ', Describe(GetTypeKind(T), SizeOf(T)));
end;

class procedure TDerived.Log(I: Integer);
begin
  Writeln('TDerived.Log(Integer): ', I);
end;

class procedure TProbe.Take(I: Integer);
begin
  Writeln('TProbe.Take(Integer): ', I);
end;

{ Does not compile for Integer: the call below must not instantiate it. }
class procedure TProbe.Take<T>(const V: T);
begin
  V.Free;
end;

var
  I: Integer;
  B: Byte;
  D: Double;
  S: string;
  Counter: TCounter;
  CounterClass: TCounterClass;
  Point: TPoint;
begin
  I := 42;
  B := 7;
  D := 2.5;
  S := 'text';

  { a class name: the non-generic method only as an equally good match }
  TOut.Show(5);
  TOut.Show(I);
  TOut.Show(S);
  TOut.Show('literal');
  TOut.Show(D);
  TOut.Show(2.5);
  TOut.Show(B);
  TOut.Show<Integer>(5);
  TOut.Show<Integer>(I);

  { an instance; unqualified inside a method }
  Counter := TCounter.Create;
  Counter.Put(I);
  Counter.Put(S);
  Counter.Put(B);
  Counter.Put<Integer>(I);
  Counter.Run;

  { a class-reference value }
  CounterClass := TCounter;
  CounterClass.Mark(I);
  CounterClass.Mark(D);
  CounterClass.Mark<Integer>(I);

  { a record value and a record type }
  Point.X := 0;
  Point.Add(I);
  Point.Add(S);
  TPoint.Show(I);
  TPoint.Show(Counter);
  Point.Run;

  { the generic method in the ancestor, the non-generic one in the descendant }
  TDerived.Log(I);
  TDerived.Log(S);
  TDerived.Log(B);
  TBase.Log(I);

  { the candidate that is not chosen leaves no trace }
  TProbe.Take(I);
  TProbe.Take(7);
  Counter.Free;
end.
