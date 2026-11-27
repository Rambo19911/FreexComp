// covers: LANG-972, LANG-973, LANG-974, LANG-975, LANG-976
program GenericDescribe;

type
  TColor = (Red, Green, Blue);
  TPlain = record
    A: Integer;
    B: Double;
  end;
  TNamed = record
    Id: Integer;
    Name: string;
  end;
  TInts = array[0..3] of Integer;
  TNames = array[0..1] of string;
  TThing = class
  end;
  TThingClass = class of TThing;
  TAction = reference to procedure;
  TCallback = procedure(X: Integer);
  TNotify = procedure(Sender: TObject) of object;
  TColors = set of TColor;

  TInfo = class
  public
    class procedure Describe<T>(const Name: string);
    class procedure Range<T>(const Name: string);
    class function Managed<T>: Boolean;
  end;

const
  Limit = 10;
  Typed: Integer = 3;
  // type queries are constant expressions outside generics too
  StringIsManaged = IsManagedType(string);
  KindOfColor = GetTypeKind(TColor);

var
  Count: Integer;

class procedure TInfo.Describe<T>(const Name: string);
const
  Managed = IsManagedType(T);
begin
  Write(Name, ': ');
  case GetTypeKind(T) of
    tkUnknown: Write('tkUnknown');
    tkInteger: Write('tkInteger');
    tkChar: Write('tkChar');
    tkEnumeration: Write('tkEnumeration');
    tkFloat: Write('tkFloat');
    tkSet: Write('tkSet');
    tkClass: Write('tkClass');
    tkMethod: Write('tkMethod');
    tkWChar: Write('tkWChar');
    tkUString: Write('tkUString');
    tkAString: Write('tkAString');
    tkSString: Write('tkSString');
    tkRecord: Write('tkRecord');
    tkArray: Write('tkArray');
    tkDynArray: Write('tkDynArray');
    tkInterface: Write('tkInterface');
    tkInt64: Write('tkInt64');
    tkPointer: Write('tkPointer');
    tkClassRef: Write('tkClassRef');
    tkProcedure: Write('tkProcedure');
  end;
  Writeln(' ', Ord(GetTypeKind(T)), ' managed=', Managed, ' weak=', HasWeakRef(T));
end;

class procedure TInfo.Range<T>(const Name: string);
begin
  Writeln(Name, ': ', Ord(Low(T)), '..', Ord(High(T)), ' size ', SizeOf(T), ' ordinal=',
    GetTypeKind(T) in [tkInteger, tkChar, tkEnumeration, tkWChar, tkInt64]);
end;

class function TInfo.Managed<T>: Boolean;
begin
  Result := IsManagedType(T) and not HasWeakRef(T);
end;

function Next: Integer;
begin
  Inc(Count);
  Result := Count;
end;

procedure Constants(Arg: Integer); inline;
begin
  Writeln('literal ', IsConstValue(42));
  Writeln('true constant ', IsConstValue(Limit));
  Writeln('constant expression ', IsConstValue(Limit * 2 + SizeOf(Integer)));
  Writeln('enumeration value ', IsConstValue(Blue));
  Writeln('type query ', IsConstValue(GetTypeKind(TNamed)));
  Writeln('variable ', IsConstValue(Count));
  Writeln('typed constant ', IsConstValue(Typed));
  Writeln('parameter ', IsConstValue(Arg));
  Writeln('function call ', IsConstValue(Next));
  Writeln('calls made ', Count);
end;

begin
  TInfo.Describe<Integer>('Integer');
  TInfo.Describe<Int64>('Int64');
  TInfo.Describe<Byte>('Byte');
  TInfo.Describe<Boolean>('Boolean');
  TInfo.Describe<TColor>('TColor');
  TInfo.Describe<Char>('Char');
  TInfo.Describe<AnsiChar>('AnsiChar');
  TInfo.Describe<Double>('Double');
  TInfo.Describe<Currency>('Currency');
  TInfo.Describe<string>('string');
  TInfo.Describe<AnsiString>('AnsiString');
  TInfo.Describe<ShortString>('ShortString');
  TInfo.Describe<TPlain>('TPlain');
  TInfo.Describe<TNamed>('TNamed');
  TInfo.Describe<TInts>('TInts');
  TInfo.Describe<TNames>('TNames');
  TInfo.Describe<TArray<Integer>>('TArray<Integer>');
  TInfo.Describe<TThing>('TThing');
  TInfo.Describe<IInterface>('IInterface');
  TInfo.Describe<TAction>('TAction');
  TInfo.Describe<Pointer>('Pointer');
  TInfo.Describe<PInteger>('PInteger');
  TInfo.Describe<TThingClass>('TThingClass');
  TInfo.Describe<TCallback>('TCallback');
  TInfo.Describe<TNotify>('TNotify');
  TInfo.Describe<TColors>('TColors');

  Constants(5);

  TInfo.Range<Byte>('Byte');
  TInfo.Range<TColor>('TColor');

  Writeln(StringIsManaged, ' ', Ord(KindOfColor), ' ', KindOfColor = tkEnumeration);
  Writeln(TInfo.Managed<TNamed>, ' ', TInfo.Managed<TPlain>);
end.
