// covers: LANG-977
program GenericPrunedBranches;

type
  TNamed = record
    Id: Integer;
    Name: string;
  end;
  TThing = class
  end;
  TCount = reference to function: Integer;

  // only a class may be its argument (the constraint is checked when an instance is made)
  TObjBox<T: class> = class
  public
    class function Describe(const V: T): string;
  end;

  TStore<T> = class
  public
    const
      Raw = not IsManagedType(T);
    class procedure Put(var Dst: T; const Src: T);
    class procedure Info(const V: T);
    class procedure OnlyNumbers(const V: T);
    class function Copies: string;
    class function Boxed(const V: T): string;
    class procedure Scratch(Factor: Integer);
    class function Measure(const V: T): TCount;
  end;

  TDemo = class
  public
    class procedure Show<T>(const Title: string; var Dst: T; const Src: T);
  end;

const
  // a type-query constant outside generics
  NamedIsManaged = IsManagedType(TNamed);

class function TObjBox<T>.Describe(const V: T): string;
begin
  Result := 'boxed ' + V.ClassName;
end;

// the example of the rule: one source, and no instance contains the other branch
class procedure TStore<T>.Put(var Dst: T; const Src: T);
begin
  if IsManagedType(T) then
  begin
    Dst := Src;
    Writeln('  managed copy');
  end
  else
  begin
    Move(Src, Dst, SizeOf(T));
    Writeln('  raw move of ', SizeOf(T), ' bytes');
  end;
end;

// each arm holds a cast that is valid for one kind of type only
class procedure TStore<T>.Info(const V: T);
begin
  case GetTypeKind(T) of
    tkInteger:
      Writeln('  integer, the next one is ', Integer(V) + 1);
    tkUString:
      Writeln('  string of length ', Length(string(V)));
    tkClass:
      Writeln('  object of class ', TObject(V).ClassName);
  else
    Writeln('  something else of ', SizeOf(T), ' bytes');
  end;
end;

// an if without else: for the other kinds nothing is left of it
class procedure TStore<T>.OnlyNumbers(const V: T);
begin
  if GetTypeKind(T) in [tkInteger, tkInt64] then
    Writeln('  twice the number is ', Integer(V) * 2);
  Writeln('  numbers checked');
end;

// conditions through type-query constants: one of the generic, one of the routine, one declared inline
class function TStore<T>.Copies: string;
const
  Small = Raw and (SizeOf(T) <= 4);
begin
  if Raw then
    Result := 'raw'
  else
    Result := 'managed';
  if Small then
    Result := Result + ', small';
  const IsObject = GetTypeKind(T) = tkClass;
  if IsObject then
    Result := Result + ', a reference to a ' + T.ClassName;
end;

// the pruned branch names an instance that could not be made: TObjBox<Integer> violates the constraint
class function TStore<T>.Boxed(const V: T): string;
begin
  if GetTypeKind(T) = tkClass then
    Result := TObjBox<T>.Describe(V)
  else
    Result := 'not boxed';
end;

// names used only in a pruned branch: a local, an inline variable, a parameter, a label
class procedure TStore<T>.Scratch(Factor: Integer);
label
  Done;
var
  Total: Integer;
begin
  if GetTypeKind(T) = tkInteger then
  begin
    Total := SizeOf(T) * Factor;
    var Half := Total div 2;
    if Half > 100 then
      goto Done;
    Writeln('  scratch ', Total, ' ', Half);
  Done:
    Writeln('  scratch done');
  end;
end;

// inside an anonymous method the type parameter is the instance's: its conditions prune too, and a variable that only
// the pruned branch names is not captured
class function TStore<T>.Measure(const V: T): TCount;
var
  Base: Integer;
begin
  Base := 100;
  Result := function: Integer
    begin
      if GetTypeKind(T) = tkUString then
        Result := Base + Length(string(V))
      else
        Result := SizeOf(T);
    end;
end;

class procedure TDemo.Show<T>(const Title: string; var Dst: T; const Src: T);
begin
  Writeln(Title, ':');
  TStore<T>.Put(Dst, Src);
  TStore<T>.Info(Dst);
  TStore<T>.OnlyNumbers(Dst);
  Writeln('  ', TStore<T>.Copies);
  Writeln('  ', TStore<T>.Boxed(Dst));
  TStore<T>.Scratch(3);
  Writeln('  measure ', TStore<T>.Measure(Dst)());
end;

var
  I, J: Integer;
  S, U: string;
  N, M: TNamed;
  O, P: TThing;
begin
  J := 41;
  TDemo.Show<Integer>('Integer', I, J);

  U := 'pruned';
  TDemo.Show<string>('string', S, U);
  Writeln('  ', S);

  M.Id := 7;
  M.Name := 'seven';
  TDemo.Show<TNamed>('TNamed', N, M);
  Writeln('  ', N.Id, ' ', N.Name);

  P := TThing.Create;
  TDemo.Show<TThing>('TThing', O, P);
  Writeln('  ', O = P);
  P.Free;

  // type-query conditions in code that is not generic: the other statement is checked and not compiled
  if IsManagedType(TNamed) then
    Writeln('TNamed is managed')
  else
    Writeln('TNamed is plain');
  if NamedIsManaged and (GetTypeKind(TThing) = tkClass) then
    Writeln('constant and query');
  case GetTypeKind(TNamed) of
    tkRecord: Writeln('TNamed is a record');
    tkClass: Writeln('TNamed is a class');
  else
    Writeln('TNamed is something else');
  end;
  if IsConstValue(I) then
    Writeln('I is a constant')
  else
    Writeln('I is a variable');
end.
